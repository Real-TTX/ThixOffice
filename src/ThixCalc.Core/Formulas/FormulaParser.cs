using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using ThixCalc.Core.Functions;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Formulas;

/// <summary>
/// Parses formula text into an AST: A1 and R1C1 references (absolute/relative, sheet-qualified with quoted names, 3D spans,
/// whole rows/columns, external workbooks), defined names, structured references, functions (localized names, _xlfn.
/// prefixes, unknown functions kept), array constants, all operators with Excel's precedence (reference operators : space
/// and union, negation, %, ^ left-associative, * /, + -, &amp;, comparisons) and the implicit intersection operator @.
/// References are stored relative to <c>origin</c> (see <see cref="CellReference"/>).
/// </summary>
public sealed class FormulaParser
{
    private readonly string _text;
    private readonly Workbook? _workbook;
    private readonly CellAddress _origin;
    private readonly FormulaSyntax _syntax;
    private readonly CalcLocale _locale;
    private int _position;

    private FormulaParser(string text, Workbook? workbook, CellAddress origin, FormulaSyntax syntax)
    {
        _text = text;
        _workbook = workbook;
        _origin = origin;
        _syntax = syntax;
        _locale = syntax.Locale;
    }

    /// <summary>Parses <paramref name="text"/> (with or without a leading "=") for a formula in <paramref name="origin"/>.</summary>
    public static Formula Parse(string text, Workbook? workbook, Worksheet? sheet, CellAddress origin, FormulaSyntax? syntax = null) =>
        new(ParseNode(text, workbook, origin, syntax));

    public static FormulaNode ParseNode(string text, Workbook? workbook, CellAddress origin, FormulaSyntax? syntax = null)
    {
        var parser = new FormulaParser(text, workbook, origin, syntax ?? FormulaSyntax.Canonical);
        return parser.ParseFormula();
    }

    /// <summary>Like <see cref="Parse"/>, but returns null and the error instead of throwing.</summary>
    public static Formula? TryParse(string text, Workbook? workbook, Worksheet? sheet, CellAddress origin, FormulaSyntax? syntax, out FormulaParseException? error)
    {
        try
        {
            error = null;
            return Parse(text, workbook, sheet, origin, syntax);
        }
        catch (FormulaParseException exception)
        {
            error = exception;
            return null;
        }
    }

    private FormulaNode ParseFormula()
    {
        SkipSpaces();
        if (Peek() == '=')
        {
            _position++;
        }

        SkipSpaces();
        if (AtEnd)
        {
            throw Error("Die Formel ist leer.");
        }

        var node = ParseExpression();
        SkipSpaces();
        if (!AtEnd)
        {
            throw Error(Peek() == ')' ? "Zu viele schließende Klammern." : $"Unerwartetes Zeichen „{Peek()}“.");
        }

        return node;
    }

    private bool AtEnd => _position >= _text.Length;

    private char Peek(int offset = 0) => _position + offset < _text.Length ? _text[_position + offset] : '\0';

    private FormulaParseException Error(string message) => new($"Diese Formel enthält einen Fehler: {message}", _position);

    private void SkipSpaces()
    {
        while (!AtEnd && IsSpace(_text[_position]))
        {
            _position++;
        }
    }

    private static bool IsSpace(char character) => character is ' ' or '\n' or '\r' or '\t' or ' ';

    // ---------------------------------------------------------------- operators by precedence (lowest first)

    private FormulaNode ParseExpression() => ParseComparison();

    private FormulaNode ParseComparison()
    {
        var left = ParseConcatenation();
        while (true)
        {
            SkipSpaces();
            BinaryOperator op;
            if (Peek() == '<' && Peek(1) == '=')
            {
                op = BinaryOperator.LessOrEqual;
                _position += 2;
            }
            else if (Peek() == '>' && Peek(1) == '=')
            {
                op = BinaryOperator.GreaterOrEqual;
                _position += 2;
            }
            else if (Peek() == '<' && Peek(1) == '>')
            {
                op = BinaryOperator.NotEqual;
                _position += 2;
            }
            else if (Peek() == '<')
            {
                op = BinaryOperator.Less;
                _position++;
            }
            else if (Peek() == '>')
            {
                op = BinaryOperator.Greater;
                _position++;
            }
            else if (Peek() == '=')
            {
                op = BinaryOperator.Equal;
                _position++;
            }
            else
            {
                return left;
            }

            left = new BinaryNode(op, left, ParseConcatenation());
        }
    }

    private FormulaNode ParseConcatenation()
    {
        var left = ParseAdditive();
        while (true)
        {
            SkipSpaces();
            if (Peek() != '&')
            {
                return left;
            }

            _position++;
            left = new BinaryNode(BinaryOperator.Concatenate, left, ParseAdditive());
        }
    }

    private FormulaNode ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (true)
        {
            SkipSpaces();
            var op = Peek() switch
            {
                '+' => BinaryOperator.Add,
                '-' => BinaryOperator.Subtract,
                _ => (BinaryOperator?)null,
            };
            if (op is null)
            {
                return left;
            }

            _position++;
            left = new BinaryNode(op.Value, left, ParseMultiplicative());
        }
    }

    private FormulaNode ParseMultiplicative()
    {
        var left = ParsePower();
        while (true)
        {
            SkipSpaces();
            var op = Peek() switch
            {
                '*' => BinaryOperator.Multiply,
                '/' => BinaryOperator.Divide,
                _ => (BinaryOperator?)null,
            };
            if (op is null)
            {
                return left;
            }

            _position++;
            left = new BinaryNode(op.Value, left, ParsePower());
        }
    }

    private FormulaNode ParsePower()
    {
        var left = ParseUnary();
        while (true)
        {
            SkipSpaces();
            if (Peek() != '^')
            {
                return left;
            }

            _position++;
            left = new BinaryNode(BinaryOperator.Power, left, ParseUnary());
        }
    }

    private FormulaNode ParseUnary()
    {
        SkipSpaces();
        if (Peek() == '-')
        {
            _position++;
            return new UnaryNode(UnaryOperator.Minus, ParseUnary());
        }

        if (Peek() == '+')
        {
            _position++;
            return new UnaryNode(UnaryOperator.Plus, ParseUnary());
        }

        return ParsePercent();
    }

    private FormulaNode ParsePercent()
    {
        var node = ParseIntersection();
        while (true)
        {
            var save = _position;
            SkipSpaces();
            if (Peek() != '%')
            {
                _position = save;
                return node;
            }

            _position++;
            node = new PercentNode(node);
        }
    }

    private FormulaNode ParseIntersection()
    {
        var left = ParseRange();
        while (IsReferenceLike(left) && !AtEnd && IsSpace(Peek()))
        {
            var save = _position;
            SkipSpaces();
            if (AtEnd || !StartsOperand(Peek()))
            {
                _position = save;
                return left;
            }

            var right = ParseRange();
            left = new BinaryNode(BinaryOperator.Intersect, left, right);
        }

        return left;
    }

    private static bool StartsOperand(char character) =>
        char.IsLetter(character) || character is '$' or '\'' or '(' or '_' or '\\' or '[' || char.IsAsciiDigit(character);

    private static bool IsReferenceLike(FormulaNode node) => node is ReferenceNode or NameNode or FunctionNode or ParenthesesNode
        or StructuredReferenceNode or ReferenceErrorNode or BinaryNode { Operator: BinaryOperator.Range or BinaryOperator.Intersect or BinaryOperator.Union };

    private FormulaNode ParseRange()
    {
        var left = SpillOperator(ParsePrimary());
        while (Peek() == ':')
        {
            _position++;
            var right = SpillOperator(ParsePrimary());
            left = CombineRange(left, right);
        }

        return left;
    }

    /// <summary>The spill range operator: A1# is ANCHORARRAY(A1) (as the file format stores it).</summary>
    private FormulaNode SpillOperator(FormulaNode node)
    {
        if (node is ReferenceNode { Area.Kind: AreaKind.Cell } && Peek() == '#' && FunctionRegistry.Find("ANCHORARRAY") is { } anchor)
        {
            _position++;
            return new FunctionNode(anchor.Name, anchor, [node]);
        }

        return node;
    }

    /// <summary>A1:B3 becomes one area reference; anything else (A1:INDEX(…)) stays a range operation.</summary>
    private static FormulaNode CombineRange(FormulaNode left, FormulaNode right)
    {
        if (left is ReferenceNode { Area.Kind: AreaKind.Cell } first && right is ReferenceNode { Area.Kind: AreaKind.Cell } last
            && (last.Sheet is null || last.Sheet == first.Sheet))
        {
            return new ReferenceNode(first.Sheet, new AreaReference(first.Area.First, last.Area.First, AreaKind.Range));
        }

        return new BinaryNode(BinaryOperator.Range, left, right);
    }

    // ---------------------------------------------------------------- operands

    private FormulaNode ParsePrimary()
    {
        SkipSpaces();
        if (AtEnd)
        {
            throw Error("Ein Operand fehlt.");
        }

        var character = Peek();
        switch (character)
        {
            case '(':
                return ParseParentheses();
            case '"':
                return new TextNode(ReadString());
            case '{':
                return ParseArray();
            case '#':
                return ParseErrorLiteral(null);
            case '@':
                _position++;
                return new UnaryNode(UnaryOperator.ImplicitIntersection, ParseRange());
            case '\'':
                return ParseQuotedSheetReference();
            case '[':
                return ParseBracketed();
        }

        if (char.IsAsciiDigit(character) || (character == _locale.DecimalSeparator && char.IsAsciiDigit(Peek(1))))
        {
            if (_syntax.Style == ReferenceStyle.A1 && TryReadRowRange(null) is { } rows)
            {
                return rows;
            }

            return new NumberNode(ReadNumber());
        }

        if (char.IsLetter(character) || character is '$' or '_' or '\\')
        {
            return ParseIdentifierOrReference();
        }

        throw Error($"Unerwartetes Zeichen „{character}“.");
    }

    private FormulaNode ParseParentheses()
    {
        _position++;
        var inner = ParseExpression();
        SkipSpaces();
        while (Peek() == _locale.ListSeparator)
        {
            _position++;
            inner = new BinaryNode(BinaryOperator.Union, inner, ParseExpression());
            SkipSpaces();
        }

        if (Peek() != ')')
        {
            throw Error("Eine schließende Klammer fehlt.");
        }

        _position++;
        return new ParenthesesNode(inner);
    }

    private string ReadString()
    {
        var builder = new StringBuilder();
        _position++;
        while (true)
        {
            if (AtEnd)
            {
                throw Error("Ein Anführungszeichen fehlt.");
            }

            var character = _text[_position++];
            if (character == '"')
            {
                if (Peek() == '"')
                {
                    builder.Append('"');
                    _position++;
                    continue;
                }

                return builder.ToString();
            }

            builder.Append(character);
        }
    }

    private double ReadNumber()
    {
        var start = _position;
        var builder = new StringBuilder();
        while (char.IsAsciiDigit(Peek()))
        {
            builder.Append(_text[_position++]);
        }

        if (Peek() == _locale.DecimalSeparator && (_locale.DecimalSeparator != _locale.ListSeparator))
        {
            _position++;
            builder.Append('.');
            while (char.IsAsciiDigit(Peek()))
            {
                builder.Append(_text[_position++]);
            }
        }

        if ((Peek() is 'E' or 'e') && (char.IsAsciiDigit(Peek(1)) || ((Peek(1) is '+' or '-') && char.IsAsciiDigit(Peek(2)))))
        {
            builder.Append('E');
            _position++;
            if (Peek() is '+' or '-')
            {
                builder.Append(_text[_position++]);
            }

            while (char.IsAsciiDigit(Peek()))
            {
                builder.Append(_text[_position++]);
            }
        }

        if (!double.TryParse(builder.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || double.IsInfinity(value))
        {
            _position = start;
            throw Error("Die Zahl ist ungültig.");
        }

        return value;
    }

    private FormulaNode ParseErrorLiteral(SheetReference? sheet)
    {
        var match = _locale.MatchError(_text, _position) ?? throw Error("Unbekannter Fehlerwert.");
        _position += match.Length;
        if (match.Error == CellError.Reference)
        {
            return sheet is null ? new ErrorNode(CellError.Reference) : new ReferenceErrorNode(sheet);
        }

        return new ErrorNode(match.Error);
    }

    private FormulaNode ParseArray()
    {
        _position++;
        var rows = new List<List<CellValue>> { new() };
        while (true)
        {
            SkipSpaces();
            rows[^1].Add(ReadArrayElement());
            SkipSpaces();
            var character = Peek();
            if (character == '}')
            {
                _position++;
                break;
            }

            if (character == _locale.ArrayColumnSeparator)
            {
                _position++;
                continue;
            }

            if (character == _locale.ArrayRowSeparator)
            {
                _position++;
                rows.Add([]);
                continue;
            }

            throw Error("Die Matrixkonstante ist ungültig.");
        }

        var columns = rows[0].Count;
        if (rows.Any(row => row.Count != columns))
        {
            throw Error("Alle Zeilen einer Matrixkonstante müssen gleich lang sein.");
        }

        return new ArrayNode(rows.Count, columns, rows.SelectMany(row => row).ToImmutableArray());
    }

    private CellValue ReadArrayElement()
    {
        var character = Peek();
        if (character == '"')
        {
            return CellValue.FromText(ReadString());
        }

        if (character == '#')
        {
            var match = _locale.MatchError(_text, _position) ?? throw Error("Unbekannter Fehlerwert.");
            _position += match.Length;
            return CellValue.FromError(match.Error);
        }

        if (character is '-' or '+')
        {
            _position++;
            SkipSpaces();
            var number = ReadNumber();
            return CellValue.FromNumber(character == '-' ? -number : number);
        }

        if (char.IsAsciiDigit(character) || character == _locale.DecimalSeparator)
        {
            return CellValue.FromNumber(ReadNumber());
        }

        var start = _position;
        while (char.IsLetter(Peek()))
        {
            _position++;
        }

        var word = _text[start.._position];
        if (IsTrue(word))
        {
            return CellValue.True;
        }

        if (IsFalse(word))
        {
            return CellValue.False;
        }

        _position = start;
        throw Error("Matrixkonstanten dürfen nur Zahlen, Texte, Wahrheitswerte und Fehlerwerte enthalten.");
    }

    private bool IsTrue(string word) =>
        string.Equals(word, _locale.TrueName, StringComparison.OrdinalIgnoreCase) || string.Equals(word, "TRUE", StringComparison.OrdinalIgnoreCase);

    private bool IsFalse(string word) =>
        string.Equals(word, _locale.FalseName, StringComparison.OrdinalIgnoreCase) || string.Equals(word, "FALSE", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- references and names

    private FormulaNode ParseIdentifierOrReference()
    {
        if (TryReadReference(null) is { } reference)
        {
            return reference;
        }

        var start = _position;
        var name = ReadName();
        if (name.Length == 0)
        {
            throw Error($"Unerwartetes Zeichen „{Peek()}“.");
        }

        // A 3D span: Tabelle1:Tabelle3!A1
        if (Peek() == ':' && !name.Contains('$'))
        {
            var save = _position;
            _position++;
            var last = ReadName();
            if (last.Length > 0 && Peek() == '!')
            {
                _position++;
                return ParseAfterSheet(ResolveSheets(name, last));
            }

            _position = save;
        }

        if (Peek() == '!')
        {
            _position++;
            return ParseAfterSheet(ResolveSheets(name, name));
        }

        if (Peek() == '(')
        {
            return ParseFunctionCall(name);
        }

        if (Peek() == '[')
        {
            return new StructuredReferenceNode(name, StructuredSpecifiers.ToCanonical(ReadBracketed()));
        }

        if (IsTrue(name))
        {
            return new BooleanNode(true);
        }

        if (IsFalse(name))
        {
            return new BooleanNode(false);
        }

        if (name.Contains('$') || name.Length == 0)
        {
            _position = start;
            throw Error("Ungültiger Bezug.");
        }

        var plain = StripPrefix(StripPrefix(name, "_xlpm."), "_xleta.");
        var function = _locale.CanonicalFunctionName(plain);
        if (FunctionRegistry.Find(function) is { IsLazy: false } && _workbook?.FindName(plain, null) is null && !name.StartsWith("_xlpm.", StringComparison.OrdinalIgnoreCase))
        {
            return new NameNode(plain) { EtaFunction = function };
        }

        return new NameNode(plain);
    }

    private string ReadName()
    {
        var start = _position;
        while (!AtEnd)
        {
            var character = _text[_position];
            if (char.IsLetterOrDigit(character) || character is '_' or '.' or '\\' or '?')
            {
                _position++;
                continue;
            }

            break;
        }

        return _text[start.._position];
    }

    private FormulaNode ParseQuotedSheetReference()
    {
        _position++;
        var builder = new StringBuilder();
        while (true)
        {
            if (AtEnd)
            {
                throw Error("Ein Apostroph fehlt.");
            }

            var character = _text[_position++];
            if (character == '\'')
            {
                if (Peek() == '\'')
                {
                    builder.Append('\'');
                    _position++;
                    continue;
                }

                break;
            }

            builder.Append(character);
        }

        if (Peek() != '!')
        {
            throw Error("Nach dem Blattnamen fehlt „!“.");
        }

        _position++;
        var sheetText = builder.ToString();
        if (sheetText.StartsWith('['))
        {
            return ReadExternalRest("'" + sheetText.Replace("'", "''", StringComparison.Ordinal) + "'!");
        }

        var colon = sheetText.IndexOf(':', StringComparison.Ordinal);
        var sheet = colon > 0 ? ResolveSheets(sheetText[..colon], sheetText[(colon + 1)..]) : ResolveSheets(sheetText, sheetText);
        return ParseAfterSheet(sheet);
    }

    /// <summary>[1]Tabelle1!A1 (external) or a structured reference without a table name ([@Menge]).</summary>
    private FormulaNode ParseBracketed()
    {
        var start = _position;
        var bracket = ReadBracketed();
        var save = _position;
        var sheet = ReadName();
        if (Peek() == '!')
        {
            _position++;
            return ReadExternalRest(bracket + sheet + "!");
        }

        _position = save;
        if (bracket.Length > 2 && bracket[1..^1].All(char.IsAsciiDigit) && Peek() == '!')
        {
            _position++;
            return ReadExternalRest(bracket + "!");
        }

        _position = save;
        if (start == _position)
        {
            throw Error("Ungültiger Bezug.");
        }

        return new StructuredReferenceNode(null, StructuredSpecifiers.ToCanonical(bracket));
    }

    private FormulaNode ReadExternalRest(string prefix)
    {
        var start = _position;
        while (!AtEnd && (char.IsLetterOrDigit(Peek()) || Peek() is '$' or ':' or '_' or '.' or '#' or '!'))
        {
            _position++;
        }

        return new ExternalReferenceNode(prefix + _text[start.._position]);
    }

    /// <summary>Reads "[...]" with nested brackets and ' escapes, returning it with its brackets.</summary>
    private string ReadBracketed()
    {
        var start = _position;
        var depth = 0;
        while (!AtEnd)
        {
            var character = _text[_position];
            if (character == '\'' && _position + 1 < _text.Length)
            {
                _position += 2;
                continue;
            }

            _position++;
            if (character == '[')
            {
                depth++;
            }
            else if (character == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return _text[start.._position];
                }
            }
        }

        throw Error("Eine schließende eckige Klammer fehlt.");
    }

    private SheetReference ResolveSheets(string first, string last)
    {
        var firstSheet = _workbook?.FindSheet(first);
        var lastSheet = _workbook?.FindSheet(last);
        if (firstSheet is null || lastSheet is null)
        {
            var name = string.Equals(first, last, StringComparison.Ordinal) ? first : first + ":" + last;
            return new SheetReference(0, 0, name);
        }

        return new SheetReference(firstSheet.Id, lastSheet.Id);
    }

    private FormulaNode ParseAfterSheet(SheetReference sheet)
    {
        if (Peek() == '#')
        {
            return ParseErrorLiteral(sheet);
        }

        if (TryReadReference(sheet) is { } reference)
        {
            return reference;
        }

        var name = ReadName();
        if (name.Length == 0)
        {
            throw Error("Nach dem Blattnamen fehlt ein Bezug.");
        }

        if (sheet.IsMissing)
        {
            return new ExternalReferenceNode(QuoteSheetName(sheet.MissingName!) + "!" + name);
        }

        return new NameNode(name, sheet.FirstSheetId);
    }

    private static string QuoteSheetName(string name) => "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";

    /// <summary>A cell, range of whole columns/rows or an R1C1 reference at the position (not a function name).</summary>
    private ReferenceNode? TryReadReference(SheetReference? sheet)
    {
        if (_syntax.Style == ReferenceStyle.R1C1)
        {
            return TryReadR1C1(sheet);
        }

        if (TryReadColumnRange(sheet) is { } columns)
        {
            return columns;
        }

        if (TryReadRowRange(sheet) is { } rows)
        {
            return rows;
        }

        var start = _position;
        if (!A1.TryReadCell(_text, ref _position, out var address, out var columnAbsolute, out var rowAbsolute))
        {
            return null;
        }

        if (IsNameContinuation(Peek()) || Peek() == '(' || Peek() == '!')
        {
            _position = start;
            return null;
        }

        var reference = CellReference.To(address, _origin, rowAbsolute, columnAbsolute);
        return new ReferenceNode(sheet, AreaReference.Cell(reference));
    }

    private static bool IsNameContinuation(char character) => char.IsLetterOrDigit(character) || character is '_' or '.' or '\\' or '?' or '[';

    private ReferenceNode? TryReadColumnRange(SheetReference? sheet)
    {
        var start = _position;
        var firstAbsolute = Peek() == '$';
        if (firstAbsolute)
        {
            _position++;
        }

        if (!A1.TryReadColumn(_text, ref _position, out var first) || Peek() != ':')
        {
            _position = start;
            return null;
        }

        _position++;
        var lastAbsolute = Peek() == '$';
        if (lastAbsolute)
        {
            _position++;
        }

        if (!A1.TryReadColumn(_text, ref _position, out var last) || IsNameContinuation(Peek()) || Peek() == '(')
        {
            _position = start;
            return null;
        }

        var firstReference = new CellReference(0, firstAbsolute ? first : first - _origin.Column, true, firstAbsolute);
        var lastReference = new CellReference(SheetLimits.LastRow, lastAbsolute ? last : last - _origin.Column, true, lastAbsolute);
        return new ReferenceNode(sheet, new AreaReference(firstReference, lastReference, AreaKind.Columns));
    }

    private ReferenceNode? TryReadRowRange(SheetReference? sheet)
    {
        var start = _position;
        var firstAbsolute = Peek() == '$';
        if (firstAbsolute)
        {
            _position++;
        }

        if (!A1.TryReadRow(_text, ref _position, out var first) || Peek() != ':')
        {
            _position = start;
            return null;
        }

        _position++;
        var lastAbsolute = Peek() == '$';
        if (lastAbsolute)
        {
            _position++;
        }

        if (!A1.TryReadRow(_text, ref _position, out var last) || IsNameContinuation(Peek()) || Peek() == '(')
        {
            _position = start;
            return null;
        }

        var firstReference = new CellReference(firstAbsolute ? first : first - _origin.Row, 0, firstAbsolute, true);
        var lastReference = new CellReference(lastAbsolute ? last : last - _origin.Row, SheetLimits.LastColumn, lastAbsolute, true);
        return new ReferenceNode(sheet, new AreaReference(firstReference, lastReference, AreaKind.Rows));
    }

    private ReferenceNode? TryReadR1C1(SheetReference? sheet)
    {
        var start = _position;
        var first = ReadR1C1Part(out var hasRow, out var hasColumn);
        if (first is null)
        {
            _position = start;
            return null;
        }

        if (IsNameContinuation(Peek()) || Peek() == '(')
        {
            _position = start;
            return null;
        }

        var kind = hasRow && hasColumn ? AreaKind.Cell : hasRow ? AreaKind.Rows : AreaKind.Columns;
        var firstReference = Complete(first.Value, kind, last: false);
        if (Peek() == ':')
        {
            var save = _position;
            _position++;
            var second = ReadR1C1Part(out var secondRow, out var secondColumn);
            var secondKind = secondRow && secondColumn ? AreaKind.Cell : secondRow ? AreaKind.Rows : AreaKind.Columns;
            if (second is not null && secondKind == kind && !IsNameContinuation(Peek()) && Peek() != '(')
            {
                var lastReference = Complete(second.Value, kind, last: true);
                return new ReferenceNode(sheet, new AreaReference(firstReference, lastReference, kind == AreaKind.Cell ? AreaKind.Range : kind));
            }

            _position = save;
        }

        if (kind != AreaKind.Cell)
        {
            return new ReferenceNode(sheet, new AreaReference(firstReference, Complete(first.Value, kind, last: true), kind));
        }

        return new ReferenceNode(sheet, AreaReference.Cell(firstReference));
    }

    private static CellReference Complete(CellReference part, AreaKind kind, bool last) => kind switch
    {
        AreaKind.Rows => part with { Column = last ? SheetLimits.LastColumn : 0, ColumnAbsolute = true },
        AreaKind.Columns => part with { Row = last ? SheetLimits.LastRow : 0, RowAbsolute = true },
        _ => part,
    };

    /// <summary>R1C1, R[-1]C, RC[2], R2 (row), C3 (column); R/C or Z/S from the locale.</summary>
    private CellReference? ReadR1C1Part(out bool hasRow, out bool hasColumn)
    {
        hasRow = false;
        hasColumn = false;
        int row = 0, column = 0;
        bool rowAbsolute = false, columnAbsolute = false;
        if (char.ToUpperInvariant(Peek()) == _locale.RowLetter)
        {
            _position++;
            hasRow = true;
            if (!ReadR1C1Number(out row, out rowAbsolute, SheetLimits.MaxRows))
            {
                return null;
            }
        }

        if (char.ToUpperInvariant(Peek()) == _locale.ColumnLetter)
        {
            _position++;
            hasColumn = true;
            if (!ReadR1C1Number(out column, out columnAbsolute, SheetLimits.MaxColumns))
            {
                return null;
            }
        }

        if (!hasRow && !hasColumn)
        {
            return null;
        }

        return new CellReference(row, column, rowAbsolute, columnAbsolute);
    }

    private bool ReadR1C1Number(out int value, out bool absolute, int limit)
    {
        value = 0;
        absolute = false;
        if (Peek() == '[')
        {
            _position++;
            var negative = Peek() == '-';
            if (negative || Peek() == '+')
            {
                _position++;
            }

            var start = _position;
            while (char.IsAsciiDigit(Peek()))
            {
                _position++;
            }

            if (start == _position || Peek() != ']' || !int.TryParse(_text.AsSpan(start, _position - start), out value) || value >= limit)
            {
                return false;
            }

            _position++;
            value = negative ? -value : value;
            return true;
        }

        var digits = _position;
        while (char.IsAsciiDigit(Peek()))
        {
            _position++;
        }

        if (digits == _position)
        {
            return true; // R or C alone: relative offset 0
        }

        if (!int.TryParse(_text.AsSpan(digits, _position - digits), out var number) || number < 1 || number > limit)
        {
            return false;
        }

        value = number - 1;
        absolute = true;
        return true;
    }

    // ---------------------------------------------------------------- functions

    private FormulaNode ParseFunctionCall(string written)
    {
        _position++;
        var arguments = ParseArguments();
        var raw = written;
        var name = StripPrefix(StripPrefix(StripPrefix(raw, "_xlfn."), "_xlws."), "_xlfn.");
        var canonical = _locale.CanonicalFunctionName(name);
        if (canonical == "SINGLE" && arguments.Length == 1)
        {
            // The file's form of the implicit intersection operator: _xlfn.SINGLE(A1:A3) is @A1:A3.
            return new UnaryNode(UnaryOperator.ImplicitIntersection, arguments[0]);
        }

        var definition = FunctionRegistry.Find(canonical);
        FormulaNode call = definition is not null
            ? new FunctionNode(definition.Name, definition, arguments)
            : new FunctionNode(raw.StartsWith("_xl", StringComparison.OrdinalIgnoreCase) ? raw : canonical, null, arguments);

        // =LAMBDA(x,x+1)(2): the result of a call is called.
        while (Peek() == '(')
        {
            _position++;
            call = new FunctionNode(string.Empty, null, ParseArguments()) { Callee = call };
        }

        return call;
    }

    private ImmutableArray<FormulaNode> ParseArguments()
    {
        var arguments = ImmutableArray.CreateBuilder<FormulaNode>();
        SkipSpaces();
        if (Peek() == ')')
        {
            _position++;
            return arguments.ToImmutable();
        }

        while (true)
        {
            SkipSpaces();
            if (Peek() == _locale.ListSeparator || Peek() == ')')
            {
                arguments.Add(MissingArgumentNode.Instance);
            }
            else
            {
                arguments.Add(ParseExpression());
                SkipSpaces();
            }

            if (Peek() == _locale.ListSeparator)
            {
                _position++;
                continue;
            }

            if (Peek() == ')')
            {
                _position++;
                return arguments.ToImmutable();
            }

            throw Error(AtEnd ? "Eine schließende Klammer fehlt." : $"Unerwartetes Zeichen „{Peek()}“ in der Argumentliste.");
        }
    }

    private static string StripPrefix(string name, string prefix) =>
        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? name[prefix.Length..] : name;
}

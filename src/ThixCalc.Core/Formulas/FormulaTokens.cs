using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Formulas;

/// <summary>What a piece of formula text is (for the editor: colours, point mode, F4, the argument tip).</summary>
public enum FormulaTokenKind : byte
{
    Other,
    Space,
    Reference,
    Function,
    Name,
    Text,
    Number,
    Operator,
    Separator,
    OpenParenthesis,
    CloseParenthesis,
    Error,
}

/// <summary>
/// A token of formula text: its place, kind and – for a reference – the range and the sheet it names (null: the formula's own
/// sheet).
/// </summary>
public sealed record FormulaToken(FormulaTokenKind Kind, int Start, int Length, CellRange? Range = null, string? Sheet = null)
{
    public int End => Start + Length;
}

/// <summary>The function the caret is in and which of its arguments (for the argument tip).</summary>
public sealed record ArgumentPosition(string Name, int Index, int NameStart);

/// <summary>
/// A tolerant scanner of formula text as typed (also unfinished): references (A1, $A$1:B2, A:C, 1:3, Tabelle2!A1, 'Mein
/// Blatt'!A1), function names before "(", names, texts, numbers, separators of the locale. It never throws; the parser
/// decides what is valid. UI-free: the desktop editor and a web editor colour and point with it.
/// </summary>
public static class FormulaTokens
{
    public static IReadOnlyList<FormulaToken> Scan(string text, CalcLocale locale)
    {
        var tokens = new List<FormulaToken>();
        var index = 0;
        while (index < text.Length)
        {
            var start = index;
            var character = text[index];
            if (char.IsWhiteSpace(character))
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                tokens.Add(new FormulaToken(FormulaTokenKind.Space, start, index - start));
                continue;
            }

            if (character == '"')
            {
                index = SkipQuoted(text, index, '"');
                tokens.Add(new FormulaToken(FormulaTokenKind.Text, start, index - start));
                continue;
            }

            if (character == '\'' || char.IsLetter(character) || character is '$' or '_' or '\\' || char.IsDigit(character))
            {
                if (TryReadReference(text, index, out var length, out var range, out var sheet))
                {
                    tokens.Add(new FormulaToken(FormulaTokenKind.Reference, start, length, range, sheet));
                    index += length;
                    continue;
                }

                if (char.IsDigit(character))
                {
                    index = ReadNumber(text, index, locale);
                    tokens.Add(new FormulaToken(FormulaTokenKind.Number, start, index - start));
                    continue;
                }

                if (character == '\'')
                {
                    index = SkipQuoted(text, index, '\'');
                    tokens.Add(new FormulaToken(FormulaTokenKind.Other, start, index - start));
                    continue;
                }

                while (index < text.Length && IsNameCharacter(text[index]))
                {
                    index++;
                }

                if (index == start)
                {
                    index++;
                }

                var next = index;
                while (next < text.Length && text[next] == ' ')
                {
                    next++;
                }

                var kind = next < text.Length && text[next] == '(' ? FormulaTokenKind.Function : FormulaTokenKind.Name;
                tokens.Add(new FormulaToken(kind, start, index - start));
                continue;
            }

            if (character == '#' && locale.MatchError(text, index) is { } error)
            {
                index += error.Length;
                tokens.Add(new FormulaToken(FormulaTokenKind.Error, start, error.Length));
                continue;
            }

            if (character == locale.DecimalSeparator && index + 1 < text.Length && char.IsDigit(text[index + 1]))
            {
                index = ReadNumber(text, index, locale);
                tokens.Add(new FormulaToken(FormulaTokenKind.Number, start, index - start));
                continue;
            }

            index++;
            var single = character switch
            {
                '(' => FormulaTokenKind.OpenParenthesis,
                ')' => FormulaTokenKind.CloseParenthesis,
                _ when character == locale.ListSeparator => FormulaTokenKind.Separator,
                '+' or '-' or '*' or '/' or '^' or '&' or '=' or '<' or '>' or '%' or ':' or '@' => FormulaTokenKind.Operator,
                _ => FormulaTokenKind.Other,
            };
            tokens.Add(new FormulaToken(single, start, 1));
        }

        return tokens;
    }

    /// <summary>
    /// Whether a reference may be pointed in at <paramref name="caret"/> (Excel's point mode): the text is a formula and the
    /// caret follows "=", an operator, "(" or a separator (spaces between allowed), or a reference just pointed in.
    /// </summary>
    public static bool CanInsertReference(string text, int caret, CalcLocale locale)
    {
        if (!text.StartsWith('=') && !text.StartsWith('+') || caret < 1)
        {
            return false;
        }

        var index = Math.Min(caret, text.Length) - 1;
        while (index > 0 && text[index] == ' ')
        {
            index--;
        }

        var character = text[index];
        if (index == 0)
        {
            return true;
        }

        if (caret < text.Length && (char.IsLetterOrDigit(text[caret]) || text[caret] is '$' or '(' or '"'))
        {
            return false;
        }

        return character is '(' or '+' or '-' or '*' or '/' or '^' or '&' or '=' or '<' or '>' or ':' || character == locale.ListSeparator;
    }

    /// <summary>The function whose argument list the caret is in, and the argument's index (null outside any).</summary>
    public static ArgumentPosition? ArgumentAt(string text, int caret, CalcLocale locale)
    {
        var stack = new Stack<(string Name, int Index, int NameStart)>();
        FormulaToken? previous = null;
        foreach (var token in Scan(text, locale))
        {
            if (token.Start >= caret)
            {
                break;
            }

            switch (token.Kind)
            {
                case FormulaTokenKind.OpenParenthesis:
                    stack.Push(previous is { Kind: FormulaTokenKind.Function } function
                        ? (text.Substring(function.Start, function.Length), 0, function.Start)
                        : (string.Empty, 0, -1));
                    break;
                case FormulaTokenKind.CloseParenthesis when stack.Count > 0:
                    stack.Pop();
                    break;
                case FormulaTokenKind.Separator when stack.Count > 0:
                    var top = stack.Pop();
                    stack.Push((top.Name, top.Index + 1, top.NameStart));
                    break;
            }

            if (token.Kind != FormulaTokenKind.Space)
            {
                previous = token;
            }
        }

        while (stack.Count > 0)
        {
            var top = stack.Pop();
            if (top.Name.Length > 0)
            {
                return new ArgumentPosition(top.Name, top.Index, top.NameStart);
            }
        }

        return null;
    }

    /// <summary>The name being typed at the caret (letters right before it, after an operator or "("), for the function list; null otherwise.</summary>
    public static (int Start, string Prefix)? NameBeforeCaret(string text, int caret)
    {
        if (!text.StartsWith('=') || caret <= 1 || caret > text.Length)
        {
            return null;
        }

        var start = caret;
        while (start > 1 && IsNameCharacter(text[start - 1]))
        {
            start--;
        }

        if (start == caret || !char.IsLetter(text[start]) || (caret < text.Length && IsNameCharacter(text[caret])))
        {
            return null;
        }

        // Not inside a text, not a sheet name.
        var quotes = text.Take(start).Count(character => character == '"');
        if (quotes % 2 == 1 || (start > 0 && text[start - 1] is '!' or '$' or '\'' ))
        {
            return null;
        }

        return (start, text[start..caret]);
    }

    /// <summary>
    /// F4: the reference at (or right before) the caret cycles A1 → $A$1 → A$1 → $A1 → A1 (every part of a range alike).
    /// Returns the new text and the caret after the reference, or null when the caret is at no reference.
    /// </summary>
    public static (string Text, int Caret, int Start, int Length)? CycleReference(string text, int caret, CalcLocale locale)
    {
        var token = Scan(text, locale).LastOrDefault(candidate => candidate.Kind == FormulaTokenKind.Reference && candidate.Start <= caret && candidate.End >= caret);
        if (token is null)
        {
            return null;
        }

        var reference = text.Substring(token.Start, token.Length);
        var bang = reference.LastIndexOf('!');
        var prefix = bang >= 0 ? reference[..(bang + 1)] : string.Empty;
        var parts = reference[prefix.Length..].Split(':');
        var cycled = string.Join(':', parts.Select(Cycle));
        var result = text[..token.Start] + prefix + cycled + text[token.End..];
        var length = prefix.Length + cycled.Length;
        return (result, token.Start + length, token.Start, length);
    }

    /// <summary>A reference as the formula shows it: "A1", "B2:C5", "Tabelle2!A1", "'Mein Blatt'!A1:B2" (whole rows/columns as 2:3 / A:C).</summary>
    public static string ReferenceText(CellRange range, string? sheet = null)
    {
        string text;
        if (range.IsWholeColumns && !range.IsWholeRows)
        {
            text = A1.ColumnName(range.FirstColumn) + ":" + A1.ColumnName(range.LastColumn);
        }
        else if (range.IsWholeRows && !range.IsWholeColumns)
        {
            text = (range.FirstRow + 1) + ":" + (range.LastRow + 1);
        }
        else
        {
            text = range.IsSingleCell ? range.First.ToString() : range.First + ":" + range.Last;
        }

        return sheet is null ? text : QuoteSheet(sheet) + "!" + text;
    }

    /// <summary>A sheet name as formulas write it: quoted when it has spaces or other characters, or looks like a reference.</summary>
    public static string QuoteSheet(string name)
    {
        var plain = name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(character => char.IsLetterOrDigit(character) || character is '_' or '.')
                    && !TryReadReference(name, 0, out _, out _, out _);
        return plain ? name : "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string Cycle(string part)
    {
        var position = 0;
        var columnAbsolute = position < part.Length && part[position] == '$';
        if (columnAbsolute)
        {
            position++;
        }

        var lettersStart = position;
        while (position < part.Length && char.IsLetter(part[position]))
        {
            position++;
        }

        var column = part[lettersStart..position];
        var rowAbsolute = position < part.Length && part[position] == '$';
        if (rowAbsolute)
        {
            position++;
        }

        var row = part[position..];
        if (column.Length == 0 || row.Length == 0)
        {
            // A whole column or row part: toggle its single $.
            var bare = part.TrimStart('$');
            return part.StartsWith('$') ? bare : "$" + bare;
        }

        // A1 → $A$1 → A$1 → $A1 → A1
        return (columnAbsolute, rowAbsolute) switch
        {
            (false, false) => "$" + column + "$" + row,
            (true, true) => column + "$" + row,
            (false, true) => "$" + column + row,
            _ => column + row,
        };
    }

    private static int SkipQuoted(string text, int index, char quote)
    {
        index++;
        while (index < text.Length)
        {
            if (text[index] == quote)
            {
                if (index + 1 < text.Length && text[index + 1] == quote)
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        return index;
    }

    private static int ReadNumber(string text, int index, CalcLocale locale)
    {
        while (index < text.Length && (char.IsDigit(text[index]) || text[index] == locale.DecimalSeparator))
        {
            index++;
        }

        if (index < text.Length && text[index] is 'e' or 'E' && index + 1 < text.Length && (char.IsDigit(text[index + 1]) || text[index + 1] is '+' or '-'))
        {
            index += 2;
            while (index < text.Length && char.IsDigit(text[index]))
            {
                index++;
            }
        }

        return index;
    }

    private static bool IsNameCharacter(char character) => char.IsLetterOrDigit(character) || character is '_' or '.' or '\\' or '?';

    /// <summary>[sheet!] then A1, A1:B2, A:B or 1:2 (each part with optional $), not followed by a name character or "(".</summary>
    private static bool TryReadReference(string text, int start, out int length, out CellRange range, out string? sheet)
    {
        length = 0;
        range = default;
        sheet = null;
        var index = start;

        // The sheet: 'quoted name'! or name!
        if (index < text.Length && text[index] == '\'')
        {
            var end = SkipQuoted(text, index, '\'');
            if (end < text.Length && text[end] == '!' && end - index >= 2)
            {
                sheet = text.Substring(index + 1, end - index - 2).Replace("''", "'", StringComparison.Ordinal);
                index = end + 1;
            }
            else
            {
                return false;
            }
        }
        else
        {
            var end = index;
            while (end < text.Length && IsNameCharacter(text[end]))
            {
                end++;
            }

            if (end < text.Length && text[end] == '!' && end > index)
            {
                sheet = text[index..end];
                index = end + 1;
            }
        }

        if (!TryReadPart(text, ref index, out var first, out var firstKind))
        {
            return false;
        }

        var last = first;
        var lastKind = firstKind;
        if (index < text.Length && text[index] == ':')
        {
            var after = index + 1;
            if (TryReadPart(text, ref after, out var second, out var secondKind) && secondKind == firstKind)
            {
                index = after;
                last = second;
                lastKind = secondKind;
            }
            else if (firstKind != PartKind.Cell)
            {
                return false;
            }
        }
        else if (firstKind != PartKind.Cell)
        {
            return false;
        }

        if (index < text.Length && (IsNameCharacter(text[index]) || text[index] == '('))
        {
            return false;
        }

        range = lastKind switch
        {
            PartKind.Column => CellRange.Columns(Math.Min(first.Column, last.Column), Math.Max(first.Column, last.Column)),
            PartKind.Row => CellRange.Rows(Math.Min(first.Row, last.Row), Math.Max(first.Row, last.Row)),
            _ => new CellRange(Math.Min(first.Row, last.Row), Math.Min(first.Column, last.Column), Math.Max(first.Row, last.Row), Math.Max(first.Column, last.Column)),
        };
        length = index - start;
        return true;
    }

    private enum PartKind : byte
    {
        Cell,
        Column,
        Row,
    }

    private static bool TryReadPart(string text, ref int index, out CellAddress address, out PartKind kind)
    {
        address = default;
        kind = PartKind.Cell;
        var position = index;
        if (position < text.Length && text[position] == '$')
        {
            position++;
        }

        var letters = position;
        while (position < text.Length && position - letters < 4 && char.IsAsciiLetter(text[position]))
        {
            position++;
        }

        var columnText = text[letters..position];
        if (position < text.Length && text[position] == '$' && columnText.Length > 0)
        {
            position++;
        }

        var digits = position;
        while (position < text.Length && position - digits < 8 && char.IsAsciiDigit(text[position]))
        {
            position++;
        }

        var rowText = text[digits..position];
        var hasColumn = columnText.Length is > 0 and <= 3 && A1.TryParseColumn(columnText.ToUpperInvariant(), out var column) && column < SheetLimits.MaxColumns;
        var hasRow = rowText.Length > 0 && int.TryParse(rowText, out var row) && row is >= 1 and <= SheetLimits.MaxRows;
        if (hasColumn && hasRow)
        {
            A1.TryParseColumn(columnText.ToUpperInvariant(), out column);
            address = new CellAddress(int.Parse(rowText, System.Globalization.CultureInfo.InvariantCulture) - 1, column);
            index = position;
            return true;
        }

        if (hasColumn && rowText.Length == 0 && columnText.Length > 0)
        {
            A1.TryParseColumn(columnText.ToUpperInvariant(), out column);
            address = new CellAddress(0, column);
            kind = PartKind.Column;
            index = position;
            return true;
        }

        if (hasRow && columnText.Length == 0)
        {
            address = new CellAddress(int.Parse(rowText, System.Globalization.CultureInfo.InvariantCulture) - 1, 0);
            kind = PartKind.Row;
            index = position;
            return true;
        }

        return false;
    }
}

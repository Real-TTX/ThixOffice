using System.Text;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>The entries of Start › Rahmen (Excel's menu).</summary>
public enum BorderPreset : byte
{
    Bottom,
    Top,
    Left,
    Right,
    None,
    All,
    Outside,
    ThickOutside,
    DoubleBottom,
    ThickBottom,
    TopAndBottom,
    TopAndThickBottom,
    TopAndDoubleBottom,
    Inside,
}

public sealed partial class EditingSession
{
    /// <summary>The font sizes Schriftgrad vergrößern/verkleinern steps through (Excel's).</summary>
    public static IReadOnlyList<double> FontSizeSteps { get; } = [8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    /// <summary>Start › Rahmen: edges of the selection's ranges (the outline only, inner lines, single sides) in a style.</summary>
    public void SetBorderPreset(BorderPreset preset, CalcColor? color = null)
    {
        var sheet = ActiveSheet;
        var thin = new BorderEdge(BorderStyle.Thin, color);
        var none = default(BorderEdge);
        Execute("Rahmen", () =>
        {
            foreach (var range in SelectedRanges)
            {
                var bounded = BoundToData(range);
                if (bounded.CellCount > 1_000_000)
                {
                    throw new BusinessException("Die Auswahl ist für diesen Befehl zu groß.");
                }

                for (var row = bounded.FirstRow; row <= bounded.LastRow; row++)
                {
                    for (var column = bounded.FirstColumn; column <= bounded.LastColumn; column++)
                    {
                        var style = sheet.GetStyle(row, column);
                        var borders = Apply(style.Borders, preset, thin, none, color,
                            top: row == range.FirstRow, bottom: row == range.LastRow, left: column == range.FirstColumn, right: column == range.LastColumn);
                        if (borders == style.Borders)
                        {
                            continue;
                        }

                        var cell = sheet.GetCell(row, column);
                        SetCell(sheet, row, column, (cell.IsSpilled ? Cell.Empty : cell) with { StyleId = Workbook.Styles.Intern(style with { Borders = borders }) });
                    }
                }
            }
        });
    }

    /// <summary>
    /// Zellen formatieren › Rahmen: the outline sides and the inner lines of every selected range; null leaves a side as it is,
    /// <see cref="BorderEdge"/> default removes it.
    /// </summary>
    public void SetBorderEdges(BorderEdge? top, BorderEdge? bottom, BorderEdge? left, BorderEdge? right, BorderEdge? insideHorizontal, BorderEdge? insideVertical)
    {
        var sheet = ActiveSheet;
        Execute("Rahmen", () =>
        {
            foreach (var range in SelectedRanges)
            {
                var bounded = BoundToData(range);
                if (bounded.CellCount > 1_000_000)
                {
                    throw new BusinessException("Die Auswahl ist für diesen Befehl zu groß.");
                }

                for (var row = bounded.FirstRow; row <= bounded.LastRow; row++)
                {
                    for (var column = bounded.FirstColumn; column <= bounded.LastColumn; column++)
                    {
                        var style = sheet.GetStyle(row, column);
                        var borders = style.Borders;
                        borders = borders with
                        {
                            Top = (row == range.FirstRow ? top : insideHorizontal) ?? borders.Top,
                            Bottom = (row == range.LastRow ? bottom : insideHorizontal) ?? borders.Bottom,
                            Left = (column == range.FirstColumn ? left : insideVertical) ?? borders.Left,
                            Right = (column == range.LastColumn ? right : insideVertical) ?? borders.Right,
                        };
                        if (borders == style.Borders)
                        {
                            continue;
                        }

                        var cell = sheet.GetCell(row, column);
                        SetCell(sheet, row, column, (cell.IsSpilled ? Cell.Empty : cell) with { StyleId = Workbook.Styles.Intern(style with { Borders = borders }) });
                    }
                }
            }
        });
    }

    /// <summary>A whole row/column selection is bounded to the used range (borders on a million empty cells help nobody).</summary>
    private CellRange BoundToData(CellRange range)
    {
        if (!range.IsWholeColumns && !range.IsWholeRows)
        {
            return range;
        }

        var used = ActiveSheet.UsedRange ?? CellRange.Cell(0, 0);
        return range.Intersect(new CellRange(0, 0, Math.Max(used.LastRow, range.IsWholeRows ? range.LastRow : 0), Math.Max(used.LastColumn, range.IsWholeColumns ? range.LastColumn : 0))) ?? CellRange.Cell(range.First);
    }

    private static CellBorders Apply(CellBorders borders, BorderPreset preset, BorderEdge thin, BorderEdge none, CalcColor? color, bool top, bool bottom, bool left, bool right)
    {
        var thick = new BorderEdge(BorderStyle.Thick, color);
        var medium = new BorderEdge(BorderStyle.Medium, color);
        var @double = new BorderEdge(BorderStyle.Double, color);
        return preset switch
        {
            BorderPreset.Bottom when bottom => borders with { Bottom = thin },
            BorderPreset.Top when top => borders with { Top = thin },
            BorderPreset.Left when left => borders with { Left = thin },
            BorderPreset.Right when right => borders with { Right = thin },
            BorderPreset.None => CellBorders.None,
            BorderPreset.All => borders with { Top = thin, Bottom = thin, Left = thin, Right = thin },
            BorderPreset.Inside => borders with
            {
                Top = top ? borders.Top : thin,
                Bottom = bottom ? borders.Bottom : thin,
                Left = left ? borders.Left : thin,
                Right = right ? borders.Right : thin,
            },
            BorderPreset.Outside => borders with
            {
                Top = top ? thin : borders.Top,
                Bottom = bottom ? thin : borders.Bottom,
                Left = left ? thin : borders.Left,
                Right = right ? thin : borders.Right,
            },
            BorderPreset.ThickOutside => borders with
            {
                Top = top ? medium : borders.Top,
                Bottom = bottom ? medium : borders.Bottom,
                Left = left ? medium : borders.Left,
                Right = right ? medium : borders.Right,
            },
            BorderPreset.DoubleBottom when bottom => borders with { Bottom = @double },
            BorderPreset.ThickBottom when bottom => borders with { Bottom = medium },
            BorderPreset.TopAndBottom => borders with { Top = top ? thin : borders.Top, Bottom = bottom ? thin : borders.Bottom },
            BorderPreset.TopAndThickBottom => borders with { Top = top ? thin : borders.Top, Bottom = bottom ? medium : borders.Bottom },
            BorderPreset.TopAndDoubleBottom => borders with { Top = top ? thin : borders.Top, Bottom = bottom ? @double : borders.Bottom },
            _ => borders,
        };
    }

    /// <summary>Einzug vergrößern / verkleinern (0–250 steps; a General alignment becomes Left, as in Excel).</summary>
    public void ChangeIndent(int delta) => ApplyStyle(style =>
    {
        var indent = Math.Clamp(style.Alignment.Indent + delta, 0, 250);
        var horizontal = style.Alignment.Horizontal is HorizontalAlignment.General or HorizontalAlignment.Center or HorizontalAlignment.Fill && indent > 0
            ? HorizontalAlignment.Left
            : style.Alignment.Horizontal;
        return style with { Alignment = style.Alignment with { Indent = indent, Horizontal = horizontal } };
    }, delta > 0 ? "Einzug vergrößern" : "Einzug verkleinern");

    /// <summary>Schriftgrad vergrößern / verkleinern: the next of <see cref="FontSizeSteps"/> from the active cell's size.</summary>
    public void ChangeFontSize(bool grow)
    {
        var size = ActiveStyle.Font.Size;
        var next = grow
            ? FontSizeSteps.FirstOrDefault(step => step > size, Math.Min(409, size + 10))
            : FontSizeSteps.LastOrDefault(step => step < size, Math.Max(1, size - 1));
        ApplyStyle(style => style with { Font = style.Font with { Size = next } }, grow ? "Schriftgrad vergrößern" : "Schriftgrad verkleinern");
    }

    public void SetStrikethrough(bool on) => ApplyStyle(style => style with { Font = style.Font with { Strikethrough = on } }, "Durchgestrichen");

    /// <summary>Dezimalstelle hinzufügen / löschen on the active cell's format (General first shows as many as the value has).</summary>
    public void ChangeDecimals(int delta)
    {
        var code = ActiveStyle.NumberFormat;
        var value = ActiveSheet.GetValue(ActiveCell);
        var changed = NumberFormatEdit.ChangeDecimals(code, delta, value.IsNumber ? value.Number : 0);
        SetNumberFormat(changed);
    }

    /// <summary>
    /// AutoSumme for one cell: the formula Excel proposes (=SUMME(B2:B9) over the numbers right above, else to the left), for
    /// the user to confirm; null when there is nothing to sum next to it.
    /// </summary>
    public string AutoSumFormula(string function)
    {
        var sheet = ActiveSheet;
        var cell = ActiveCell;
        var range = NumbersNext(sheet, cell, vertical: true) ?? NumbersNext(sheet, cell, vertical: false);
        var name = Locale.FunctionName(function);
        return range is { } found ? $"={name}({FormulaTokens.ReferenceText(found)})" : $"={name}()";
    }

    /// <summary>The run of numbers above (or left of) a cell, skipping blanks right next to it, as Excel's AutoSumme finds it.</summary>
    private static CellRange? NumbersNext(Worksheet sheet, CellAddress cell, bool vertical)
    {
        var index = vertical ? cell.Row - 1 : cell.Column - 1;
        CellValue At(int position) => vertical ? sheet.GetValue(position, cell.Column) : sheet.GetValue(cell.Row, position);
        while (index >= 0 && At(index).IsBlank)
        {
            index--;
        }

        if (index < 0 || !At(index).IsNumber)
        {
            return null;
        }

        var last = index;
        while (index > 0 && (At(index - 1).IsNumber || At(index - 1).IsBlank && index - 1 > 0 && At(index - 2).IsNumber))
        {
            index--;
        }

        return vertical ? new CellRange(index, cell.Column, last, cell.Column) : new CellRange(cell.Row, index, cell.Row, last);
    }

    /// <summary>
    /// AutoSumme over a selected range: a total below each column of numbers (or right of each row when the selection is one
    /// row), as Excel does; one undo step.
    /// </summary>
    public void AutoSum(string function)
    {
        var sheet = ActiveSheet;
        var range = Selection;
        var name = Locale.FunctionName(function);
        Execute("AutoSumme", () =>
        {
            if (range.RowCount == 1)
            {
                var target = new CellAddress(range.FirstRow, range.LastColumn + 1);
                EnterInto(sheet, target, $"={name}({FormulaTokens.ReferenceText(range)})");
                return;
            }

            // The last row is the totals row when it is empty, else the row below.
            var lastIsEmpty = Enumerable.Range(range.FirstColumn, range.ColumnCount).All(column => sheet.GetValue(range.LastRow, column).IsBlank);
            var totalRow = lastIsEmpty ? range.LastRow : range.LastRow + 1;
            var dataLast = lastIsEmpty ? range.LastRow - 1 : range.LastRow;
            for (var column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                var hasNumbers = Enumerable.Range(range.FirstRow, Math.Max(0, dataLast - range.FirstRow + 1)).Any(row => sheet.GetValue(row, column).IsNumber);
                if (hasNumbers)
                {
                    EnterInto(sheet, new CellAddress(totalRow, column), $"={name}({FormulaTokens.ReferenceText(new CellRange(range.FirstRow, column, dataLast, column))})");
                }
            }
        });
    }

    /// <summary>
    /// Daten › Duplikate entfernen: rows of the range whose values in <paramref name="columns"/> (offsets) repeat an earlier row
    /// are removed, the rows below move up inside the range; returns how many were removed.
    /// </summary>
    public int RemoveDuplicates(CellRange range, IReadOnlyList<int> columns, bool hasHeader)
    {
        var sheet = ActiveSheet;
        var first = hasHeader ? range.FirstRow + 1 : range.FirstRow;
        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var keep = new List<int>();
        for (var row = first; row <= range.LastRow; row++)
        {
            var key = string.Join('\u001F', columns.Select(offset => sheet.GetValue(row, range.FirstColumn + offset)).Select(value => value.Kind + ":" + (value.IsNumber ? value.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : value.Text)));
            if (seen.Add(key))
            {
                keep.Add(row);
            }
        }

        var removed = range.LastRow - first + 1 - keep.Count;
        if (removed == 0)
        {
            return 0;
        }

        var rows = keep.Select(row => Enumerable.Range(range.FirstColumn, range.ColumnCount).Select(column => sheet.GetCell(row, column)).ToList()).ToList();
        Execute("Duplikate entfernen", () =>
        {
            GuardArrays(sheet, range);
            for (var index = 0; index <= range.LastRow - first; index++)
            {
                for (var column = 0; column < range.ColumnCount; column++)
                {
                    var target = index < rows.Count ? rows[index][column] : Cell.Empty with { StyleId = sheet.GetStyleId(first + index, range.FirstColumn + column) };
                    SetCell(sheet, first + index, range.FirstColumn + column, target.IsSpilled ? Cell.Empty : target);
                }
            }
        });
        return removed;
    }

    /// <summary>
    /// Daten › Text in Spalten: each cell of the selection's first column is split at <paramref name="separators"/> (consecutive
    /// ones as one when asked) into the columns to its right; each part is read like typed input.
    /// </summary>
    public void TextToColumns(CellRange range, IReadOnlyCollection<char> separators, bool mergeConsecutive = false, char? quote = '"')
    {
        if (separators.Count == 0)
        {
            throw new BusinessException("Wählen Sie mindestens ein Trennzeichen.", "separators");
        }

        var sheet = ActiveSheet;
        var column = range.FirstColumn;
        Execute("Text in Spalten", () =>
        {
            for (var row = range.FirstRow; row <= range.LastRow; row++)
            {
                var value = sheet.GetValue(row, column);
                if (!value.IsText || sheet.GetFormula(row, column) is not null)
                {
                    continue;
                }

                var parts = Split(value.Text, separators, mergeConsecutive, quote);
                for (var index = 0; index < parts.Count && column + index <= SheetLimits.LastColumn; index++)
                {
                    EnterInto(sheet, new CellAddress(row, column + index), parts[index].StartsWith('=') ? "'" + parts[index] : parts[index]);
                }
            }
        });
    }

    private static List<string> Split(string text, IReadOnlyCollection<char> separators, bool mergeConsecutive, char? quote)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quote is { } mark && character == mark)
            {
                quoted = !quoted;
                continue;
            }

            if (!quoted && separators.Contains(character))
            {
                if (!(mergeConsecutive && current.Length == 0 && parts.Count > 0))
                {
                    parts.Add(current.ToString());
                }

                current.Clear();
                continue;
            }

            current.Append(character);
        }

        parts.Add(current.ToString());
        return parts;
    }

    /// <summary>Optimale Höhe: rows fit their tallest wrapped or large text (the default height when they hold none).</summary>
    public void AutoFitRows(int first, int last, GridMetrics metrics)
    {
        var sheet = ActiveSheet;
        var heights = new Dictionary<int, double>();
        for (var row = first; row <= Math.Min(last, first + 10_000); row++)
        {
            var tallest = sheet.Rows.DefaultSize;
            foreach (var entry in sheet.GetCells(CellRange.Rows(row, row)))
            {
                var style = sheet.GetStyle(entry.Row, entry.Column);
                var lineHeight = style.Font.Size * 1.32;
                var lines = 1;
                if (style.Alignment.WrapText && !entry.Cell.Value.IsBlank)
                {
                    var width = metrics.ColumnPixels(sheet.Columns[entry.Column].Size ?? sheet.Columns.DefaultSize) * 0.75 - 4;
                    var text = NumberFormatter.Format(entry.Cell.Value, style.NumberFormat, metrics.Locale, Workbook.Date1904).Text;
                    var font = metrics.FontOf(style.Font);
                    lines = text.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(metrics.Measure(line, font) / Math.Max(1, width))));
                }
                else if (entry.Cell.Value.IsText)
                {
                    lines = entry.Cell.Value.Text.Count(character => character == '\n') + 1;
                }

                tallest = Math.Max(tallest, Math.Ceiling(lineHeight * lines * 4) / 4);
            }

            heights[row] = Math.Min(409, tallest);
        }

        Execute("Optimale Höhe", () =>
        {
            var axis = sheet.Rows;
            var before = axis.Snapshot();
            foreach (var (row, height) in heights)
            {
                axis.Set(row, axis[row] with { Size = Math.Abs(height - axis.DefaultSize) < 0.01 ? null : height, CustomSize = false });
            }

            var after = axis.Snapshot();
            var size = axis.DefaultSize;
            Record(new ActionStep(() => axis.Restore(before, size), () => axis.Restore(after, size)));
            Workbook.NoteStructureChanged();
        });
    }
}

/// <summary>Edits of number format codes the ribbon's buttons make (canonical codes in, canonical codes out).</summary>
public static class NumberFormatEdit
{
    /// <summary>One decimal more (<paramref name="delta"/> &gt; 0) or less in every numeric section of <paramref name="code"/>.</summary>
    public static string ChangeDecimals(string code, int delta, double value = 0)
    {
        if (NumberFormat.Parse(code).IsGeneral)
        {
            var text = value.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
            var decimals = text.Contains('.') ? text.Length - text.IndexOf('.') - 1 : 0;
            decimals = Math.Max(0, decimals + delta);
            return decimals == 0 ? "0" : "0." + new string('0', decimals);
        }

        if (NumberFormat.Parse(code).IsDateTime || NumberFormat.Parse(code).IsText)
        {
            return code;
        }

        return string.Join(';', SplitSections(code).Select(section => ChangeSection(section, delta)));
    }

    private static IEnumerable<string> SplitSections(string code)
    {
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];
            if (character == '"')
            {
                quoted = !quoted;
            }

            if (character == '\\' && index + 1 < code.Length)
            {
                current.Append(character).Append(code[++index]);
                continue;
            }

            if (character == ';' && !quoted)
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        yield return current.ToString();
    }

    private static string ChangeSection(string section, int delta)
    {
        // The place of the last digit placeholder outside quotes and brackets.
        var lastDigit = -1;
        var point = -1;
        var quoted = false;
        var bracket = false;
        for (var index = 0; index < section.Length; index++)
        {
            var character = section[index];
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && character == '[')
            {
                bracket = true;
            }
            else if (!quoted && character == ']')
            {
                bracket = false;
            }
            else if (!quoted && !bracket && character == '\\')
            {
                index++;
            }
            else if (!quoted && !bracket && character is '0' or '#' or '?')
            {
                lastDigit = index;
            }
            else if (!quoted && !bracket && character == '.' && point < 0)
            {
                point = index;
            }
        }

        if (lastDigit < 0)
        {
            return section;
        }

        if (delta > 0)
        {
            return point >= 0 && point < lastDigit
                ? section.Insert(lastDigit + 1, "0")
                : section.Insert(lastDigit + 1, ".0");
        }

        if (point < 0 || point > lastDigit)
        {
            return section;
        }

        // Remove one placeholder after the point (and the point with the last one).
        var decimals = lastDigit - point;
        return decimals <= 1 ? section.Remove(point, lastDigit - point + 1) : section.Remove(lastDigit, 1);
    }
}

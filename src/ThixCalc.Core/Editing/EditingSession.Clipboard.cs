using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>What Inhalte einfügen pastes.</summary>
public enum PasteMode : byte
{
    All,
    Values,
    Formulas,
    Formats,
    ValuesAndNumberFormats,
    AllExceptBorders,
}

/// <summary>Inhalte einfügen › Vorgang: what pasted numbers do with the numbers already there.</summary>
public enum PasteOperation : byte
{
    None,
    Add,
    Subtract,
    Multiply,
    Divide,
}

/// <summary>Options of Inhalte einfügen.</summary>
public sealed record PasteOptions(PasteMode Mode = PasteMode.All, bool Transpose = false, bool SkipBlanks = false, PasteOperation Operation = PasteOperation.None);

/// <summary>
/// Copied or cut cells: a snapshot of the source range (values, formulas, styles, merges, column widths) and its text for
/// other programs (tab-separated displayed values). Formulas are position-independent, so pasting adjusts relative
/// references by itself.
/// </summary>
public sealed class ClipboardContent
{
    internal ClipboardContent(Worksheet sheet, CellRange range, Cell[,] cells, IReadOnlyList<CellRange> merges, string text, bool cut)
    {
        Sheet = sheet;
        Range = range;
        Cells = cells;
        Merges = merges;
        Text = text;
        IsCut = cut;
    }

    public Worksheet Sheet { get; }

    public CellRange Range { get; }

    /// <summary>The cells relative to the range's first cell.</summary>
    internal Cell[,] Cells { get; }

    /// <summary>Merges inside the range, relative to its first cell.</summary>
    internal IReadOnlyList<CellRange> Merges { get; }

    /// <summary>Tab-separated displayed values, rows separated by CR LF (for other programs).</summary>
    public string Text { get; }

    public bool IsCut { get; }
}

public sealed partial class EditingSession
{
    public ClipboardContent Copy() => Capture(cut: false);

    /// <summary>Ausschneiden: pasting moves the cells (references to them follow).</summary>
    public ClipboardContent Cut() => Capture(cut: true);

    private ClipboardContent Capture(bool cut)
    {
        if (SelectedRanges.Count > 1)
        {
            throw new BusinessException("Dieser Befehl kann nicht für Mehrfachauswahlen verwendet werden.");
        }

        var sheet = ActiveSheet;
        var range = Selection;
        if (range.IsWholeColumns || range.IsWholeRows)
        {
            range = sheet.UsedRange?.Intersect(range) ?? CellRange.Cell(range.First);
        }

        if (range.CellCount > 5_000_000)
        {
            throw new BusinessException("Die Auswahl ist für diesen Befehl zu groß.");
        }

        var cells = new Cell[range.RowCount, range.ColumnCount];
        foreach (var entry in sheet.GetCells(range))
        {
            cells[entry.Row - range.FirstRow, entry.Column - range.FirstColumn] = entry.Cell;
        }

        var merges = sheet.Merges.Intersecting(range).Where(range.Contains).Select(merge => merge.Offset(-range.FirstRow, -range.FirstColumn)).ToList();
        var text = new StringBuilder();
        for (var row = range.FirstRow; row <= range.LastRow; row++)
        {
            for (var column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                if (column > range.FirstColumn)
                {
                    text.Append('\t');
                }

                var display = NumberFormatter.Format(sheet.GetValue(row, column), sheet.GetStyle(row, column).NumberFormat, Locale, Workbook.Date1904).Text;
                text.Append(display.Contains('\n') || display.Contains('\t') || display.Contains('"') ? "\"" + display.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : display);
            }

            text.Append("\r\n");
        }

        return new ClipboardContent(sheet, range, cells, merges, text.ToString(), cut);
    }

    /// <summary>Einfügen into the selection (the content repeats when the selection is a multiple of it).</summary>
    public void Paste(ClipboardContent content, PasteOptions? options = null)
    {
        options ??= new PasteOptions();
        if (content.IsCut)
        {
            PasteCut(content);
            return;
        }

        var sheet = ActiveSheet;
        var rows = options.Transpose ? content.Range.ColumnCount : content.Range.RowCount;
        var columns = options.Transpose ? content.Range.RowCount : content.Range.ColumnCount;
        var target = Selection;
        var repeatRows = target.RowCount % rows == 0 ? target.RowCount / rows : 1;
        var repeatColumns = target.ColumnCount % columns == 0 ? target.ColumnCount / columns : 1;
        if (target.IsSingleCell || (long)repeatRows * repeatColumns > 10_000)
        {
            repeatRows = repeatColumns = 1;
        }

        var first = target.First;
        var area = new CellRange(first.Row, first.Column, first.Row + rows * repeatRows - 1, first.Column + columns * repeatColumns - 1);
        if (area.LastRow > SheetLimits.LastRow || area.LastColumn > SheetLimits.LastColumn)
        {
            throw new BusinessException("Die Daten können nicht eingefügt werden, weil der Kopierbereich und der Einfügebereich nicht dieselbe Größe haben.");
        }

        var sameWorkbook = content.Sheet.Workbook == Workbook;
        Execute("Einfügen", () =>
        {
            GuardArrays(sheet, area);
            for (var repeatRow = 0; repeatRow < repeatRows; repeatRow++)
            {
                for (var repeatColumn = 0; repeatColumn < repeatColumns; repeatColumn++)
                {
                    for (var row = 0; row < content.Range.RowCount; row++)
                    {
                        for (var column = 0; column < content.Range.ColumnCount; column++)
                        {
                            var source = content.Cells[row, column];
                            var (targetRow, targetColumn) = options.Transpose ? (column, row) : (row, column);
                            var address = new CellAddress(first.Row + repeatRow * rows + targetRow, first.Column + repeatColumn * columns + targetColumn);
                            if (options.SkipBlanks && source.Value.IsBlank && source.Formula is null)
                            {
                                continue;
                            }

                            var origin = new CellAddress(content.Range.FirstRow + row, content.Range.FirstColumn + column);
                            var pasted = PastedCell(sheet, address, source, origin, content, options, sameWorkbook);
                            SetCell(sheet, address.Row, address.Column, Operate(sheet.GetCell(address), pasted, options.Operation));
                        }
                    }

                    if (options.Mode is PasteMode.All or PasteMode.Formats or PasteMode.AllExceptBorders && !options.Transpose && content.Merges.Count > 0)
                    {
                        var offsetRow = first.Row + repeatRow * rows;
                        var offsetColumn = first.Column + repeatColumn * columns;
                        ChangeSheetState(sheet, () =>
                        {
                            foreach (var merge in content.Merges)
                            {
                                sheet.Merges.Add(merge.Offset(offsetRow, offsetColumn));
                            }
                        });
                    }
                }
            }

            SetSelection(first, [area]);
        });
    }

    /// <summary>Vorgang: a pasted number combined with the number (or blank) already in the cell; anything else is pasted as it is.</summary>
    private static Cell Operate(Cell existing, Cell pasted, PasteOperation operation)
    {
        if (operation == PasteOperation.None || pasted.Formula is not null || !pasted.Value.IsNumber
            || existing.Formula is not null || !(existing.Value.IsNumber || existing.Value.IsBlank))
        {
            return operation == PasteOperation.None || !pasted.Value.IsBlank ? pasted : existing;
        }

        var old = existing.Value.IsNumber ? existing.Value.Number : 0;
        var number = pasted.Value.Number;
        var result = operation switch
        {
            PasteOperation.Add => CellValue.FromNumber(old + number),
            PasteOperation.Subtract => CellValue.FromNumber(old - number),
            PasteOperation.Multiply => CellValue.FromNumber(old * number),
            _ => number == 0 ? CellValue.FromError(CellError.DivisionByZero) : CellValue.FromNumber(old / number),
        };
        return pasted with { Value = result };
    }

    private Cell PastedCell(Worksheet sheet, CellAddress address, Cell source, CellAddress origin, ClipboardContent content, PasteOptions options, bool sameWorkbook)
    {
        var existing = sheet.GetCell(address);
        var style = sameWorkbook ? source.StyleId : Workbook.Styles.Intern(content.Sheet.Workbook.Styles[source.StyleId]);
        var sourceStyle = content.Sheet.Workbook.Styles[source.StyleId];
        var value = source.IsSpilled ? source.Value : source.Value;
        var formula = source.IsSpilled ? null : source.Formula;
        if (formula is not null && (!sameWorkbook || options.Transpose || content.Sheet != sheet && formula.Precedents.Any(node => node is ReferenceNode { Sheet: null })))
        {
            formula = Translate(formula, content.Sheet, origin, sheet, options.Transpose);
        }

        switch (options.Mode)
        {
            case PasteMode.Values:
                return existing with { Value = value, Formula = null, IsSpilled = false, ArrayRange = null };
            case PasteMode.ValuesAndNumberFormats:
                return new Cell(value, null, Workbook.Styles.Intern(sheet.GetStyle(address.Row, address.Column) with { NumberFormat = sourceStyle.NumberFormat }));
            case PasteMode.Formulas:
                return new Cell(formula is null ? value : CellValue.Blank, formula, existing.StyleId, IsDynamicArray: source.IsDynamicArray || formula is not null);
            case PasteMode.Formats:
                return (existing.IsSpilled ? Cell.Empty : existing) with { StyleId = style };
            case PasteMode.AllExceptBorders:
                var withoutBorders = Workbook.Styles.Intern(Workbook.Styles[style] with { Borders = sheet.GetStyle(address.Row, address.Column).Borders });
                return new Cell(formula is null ? value : CellValue.Blank, formula, withoutBorders, IsDynamicArray: source.IsDynamicArray || formula is not null);
            default:
                return new Cell(formula is null ? value : CellValue.Blank, formula, style, IsDynamicArray: source.IsDynamicArray || formula is not null);
        }
    }

    /// <summary>A formula moved to another workbook/sheet context (or transposed): through its text, references swapped when transposing.</summary>
    private Formula Translate(Formula formula, Worksheet sourceSheet, CellAddress origin, Worksheet target, bool transpose)
    {
        var root = formula.Root;
        if (transpose)
        {
            root = TransposeReferences(root);
        }

        var text = FormulaFormatter.Format(root, sourceSheet.Workbook, origin);
        if (sourceSheet.Workbook == Workbook && sourceSheet != target)
        {
            // Own-sheet references keep pointing at cells of the sheet they are pasted into (as Excel does).
        }

        try
        {
            return FormulaParser.Parse(text, Workbook, target, origin);
        }
        catch (FormulaParseException)
        {
            return formula;
        }
    }

    private static FormulaNode TransposeReferences(FormulaNode node) => node switch
    {
        ReferenceNode reference => reference with
        {
            Area = new AreaReference(Swap(reference.Area.First), Swap(reference.Area.Last), reference.Area.Kind switch
            {
                AreaKind.Rows => AreaKind.Columns,
                AreaKind.Columns => AreaKind.Rows,
                _ => reference.Area.Kind,
            }),
        },
        FunctionNode function => function with { Arguments = [.. function.Arguments.Select(TransposeReferences)] },
        BinaryNode binary => binary with { Left = TransposeReferences(binary.Left), Right = TransposeReferences(binary.Right) },
        UnaryNode unary => unary with { Operand = TransposeReferences(unary.Operand) },
        PercentNode percent => percent with { Operand = TransposeReferences(percent.Operand) },
        ParenthesesNode parentheses => parentheses with { Inner = TransposeReferences(parentheses.Inner) },
        _ => node,
    };

    /// <summary>Relative offsets swap rows and columns; absolute coordinates stay.</summary>
    private static CellReference Swap(CellReference reference) => new(
        reference.ColumnAbsolute ? reference.Row : reference.Column,
        reference.RowAbsolute ? reference.Column : reference.Row,
        reference.RowAbsolute,
        reference.ColumnAbsolute);

    /// <summary>Pasting cut cells moves them: references to them follow, formulas in them keep their targets.</summary>
    private void PasteCut(ClipboardContent content)
    {
        var source = content.Range;
        var sourceSheet = content.Sheet;
        var targetSheet = ActiveSheet;
        var first = Selection.First;
        var target = new CellRange(first.Row, first.Column, first.Row + source.RowCount - 1, first.Column + source.ColumnCount - 1);
        if (target.LastRow > SheetLimits.LastRow || target.LastColumn > SheetLimits.LastColumn)
        {
            throw new BusinessException("Die Daten können hier nicht eingefügt werden.");
        }

        Execute("Ausschneiden und einfügen", () =>
        {
            var snapshot = new WorkbookSnapshot(Workbook);
            List<(int Row, int Column, Cell Cell)> overwritten = [];
            void MoveRecords(Worksheet from, CellRange fromRange, Worksheet to, CellAddress toFirst)
            {
                var records = from.Store.Enumerate(fromRange).ToList();
                foreach (var (row, column, _) in records)
                {
                    from.Store.Remove(row, column);
                }

                foreach (var (row, column, record) in records)
                {
                    to.Store.Set(row - fromRange.FirstRow + toFirst.Row, column - fromRange.FirstColumn + toFirst.Column, record);
                }
            }

            void Forward()
            {
                overwritten = CaptureCells(targetSheet, target).Where(cell => !(sourceSheet == targetSheet && source.Contains(cell.Row, cell.Column))).ToList();
                ReferenceRewriter.ApplyToWorkbook(Workbook, new RangeMove(sourceSheet.Id, source, targetSheet.Id, first));
                foreach (var (row, column, _) in overwritten)
                {
                    targetSheet.Store.Remove(row, column);
                }

                MoveRecords(sourceSheet, source, targetSheet, first);
                Workbook.Calculation.Rebuild();
            }

            void Backward()
            {
                MoveRecords(targetSheet, target, sourceSheet, source.First);
                RestoreCells(targetSheet, overwritten);
                snapshot.Restore(Workbook);
                Workbook.Calculation.Rebuild();
            }

            Forward();
            Record(new ActionStep(Backward, Forward, structural: true));
            Workbook.NoteStructureChanged();
            SetSelection(first, [target]);
        });
    }

    /// <summary>Text from another program (tab-separated rows): each field recognized like typed input.</summary>
    public void PasteText(string text)
    {
        var sheet = ActiveSheet;
        var first = Selection.First;
        var lines = Formats.Csv.CsvFormat.Parse(text.TrimEnd('\r', '\n'), '\t').ToList();
        Execute("Einfügen", () =>
        {
            for (var row = 0; row < lines.Count; row++)
            {
                for (var column = 0; column < lines[row].Count; column++)
                {
                    var address = new CellAddress(first.Row + row, first.Column + column);
                    if (address.IsValid)
                    {
                        var field = lines[row][column].Text;
                        EnterInto(sheet, address, field.StartsWith('=') ? "'" + field : field);
                    }
                }
            }
        });
    }

    // ---------------------------------------------------------------- fill and series

    /// <summary>Strg+U (Ausfüllen nach unten): the first row of each selected range is copied into the others.</summary>
    public void FillDown() => Fill(vertical: true);

    /// <summary>Strg+R (Ausfüllen nach rechts).</summary>
    public void FillRight() => Fill(vertical: false);

    private void Fill(bool vertical)
    {
        var sheet = ActiveSheet;
        Execute(vertical ? "Unten ausfüllen" : "Rechts ausfüllen", () =>
        {
            foreach (var range in SelectedRanges)
            {
                var source = vertical ? new CellRange(range.FirstRow, range.FirstColumn, range.FirstRow, range.LastColumn) : new CellRange(range.FirstRow, range.FirstColumn, range.LastRow, range.FirstColumn);
                CopyInto(sheet, source, range);
            }
        });
    }

    private void CopyInto(Worksheet sheet, CellRange source, CellRange target)
    {
        GuardArrays(sheet, target);
        for (var row = target.FirstRow; row <= target.LastRow; row++)
        {
            for (var column = target.FirstColumn; column <= target.LastColumn; column++)
            {
                if (source.Contains(row, column))
                {
                    continue;
                }

                var sourceRow = source.FirstRow + (row - target.FirstRow) % source.RowCount;
                var sourceColumn = source.FirstColumn + (column - target.FirstColumn) % source.ColumnCount;
                var cell = sheet.GetCell(sourceRow, sourceColumn);
                SetCell(sheet, row, column, cell.IsSpilled ? Cell.Empty with { StyleId = cell.StyleId } : cell);
            }
        }
    }

    /// <summary>
    /// AutoAusfüllen (dragging the fill handle) from <paramref name="source"/> to <paramref name="target"/> (which contains it):
    /// numbers continue their linear trend (one number is copied, or counts with <paramref name="increment"/> = Strg),
    /// dates step by days or months, texts ending in a number count up, weekday and month names (German and English) continue,
    /// formulas are copied with relative references adjusted, everything else repeats.
    /// </summary>
    public void AutoFill(CellRange source, CellRange target, bool increment = false)
    {
        var sheet = ActiveSheet;
        if (!target.Contains(source))
        {
            throw new BusinessException("Der Zielbereich muss den Ausgangsbereich enthalten.");
        }

        var vertical = target.ColumnCount == source.ColumnCount;
        var backwards = vertical ? target.FirstRow < source.FirstRow : target.FirstColumn < source.FirstColumn;
        Execute("AutoAusfüllen", () =>
        {
            GuardArrays(sheet, target);
            var lines = vertical ? source.ColumnCount : source.RowCount;
            for (var line = 0; line < lines; line++)
            {
                var cells = new List<Cell>();
                var length = vertical ? source.RowCount : source.ColumnCount;
                for (var index = 0; index < length; index++)
                {
                    cells.Add(vertical ? sheet.GetCell(source.FirstRow + index, source.FirstColumn + line) : sheet.GetCell(source.FirstRow + line, source.FirstColumn + index));
                }

                var series = Series.Create(cells, increment, Locale, sheet, Workbook);
                var count = vertical ? target.RowCount - source.RowCount : target.ColumnCount - source.ColumnCount;
                for (var step = 1; step <= count; step++)
                {
                    var position = backwards ? -step : length - 1 + step;
                    var cell = series.At(position);
                    var address = vertical
                        ? new CellAddress(backwards ? source.FirstRow - step : source.LastRow + step, source.FirstColumn + line)
                        : new CellAddress(source.FirstRow + line, backwards ? source.FirstColumn - step : source.LastColumn + step);
                    SetCell(sheet, address.Row, address.Column, cell);
                }
            }

            SetSelection(ActiveCell, [target]);
        });
    }

    /// <summary>The continuation of a line of cells for AutoFill.</summary>
    private sealed class Series
    {
        private static readonly Regex TrailingNumber = new(@"^(.*?)(\d+)(\D*)$", RegexOptions.CultureInvariant);

        private readonly List<Cell> _cells;
        private readonly Func<int, Cell>? _generator;

        private Series(List<Cell> cells, Func<int, Cell>? generator)
        {
            _cells = cells;
            _generator = generator;
        }

        public static Series Create(List<Cell> cells, bool increment, Globalization.CalcLocale locale, Worksheet sheet, Workbook workbook)
        {
            var styles = cells.Select(cell => cell.StyleId).ToList();
            Cell Styled(CellValue value, int position) => new(value, null, styles[Modulo(position, styles.Count)]);

            if (cells.Any(cell => cell.Formula is not null || cell.IsSpilled))
            {
                return new Series(cells, null);
            }

            var values = cells.Select(cell => cell.Value).ToList();
            if (values.All(value => value.IsNumber))
            {
                var format = NumberFormat.Parse(workbook.Styles[styles[0]].NumberFormat);
                if (values.Count == 1 && !increment && !format.IsDateTime)
                {
                    return new Series(cells, null);
                }

                if (format.IsDateTime && values.Count >= 2 && MonthStep(values, workbook.Date1904) is { } months)
                {
                    var (year, month, day) = DateSerial.ToDate(values[0].Number, workbook.Date1904);
                    return new Series(cells, position =>
                    {
                        var total = year * 12 + month - 1 + months * position;
                        var targetYear = total / 12;
                        var targetMonth = total % 12 + 1;
                        var targetDay = Math.Min(day, DateTime.DaysInMonth(Math.Clamp(targetYear, 1, 9999), targetMonth));
                        return Styled(DateSerial.FromDate(targetYear, targetMonth, targetDay, workbook.Date1904) ?? CellValue.FromError(CellError.Number), position);
                    });
                }

                var (slope, intercept) = values.Count == 1 ? (1.0, values[0].Number) : Trend(values);
                return new Series(cells, position => Styled(CellValue.FromNumber(Functions.MathFunctions.Round(intercept + slope * position, 10)), position));
            }

            if (values.All(value => value.IsText) && NameList(values, locale) is { } names)
            {
                var (list, start, step) = names;
                return new Series(cells, position => Styled(list[Modulo(start + step * position, list.Count)], position));
            }

            if (values.All(value => value.IsText) && values.Select(value => TrailingNumber.Match(value.Text)).All(match => match.Success))
            {
                var matches = values.Select(value => TrailingNumber.Match(value.Text)).ToList();
                var numbers = matches.Select(match => double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)).ToList();
                var step = numbers.Count > 1 ? (numbers[^1] - numbers[0]) / (numbers.Count - 1) : 1;
                var digits = matches[0].Groups[2].Value.Length;
                var prefix = matches[0].Groups[1].Value;
                var suffix = matches[0].Groups[3].Value;
                return new Series(cells, position =>
                {
                    var number = Math.Abs(numbers[0] + step * position);
                    var text = ((long)number).ToString(CultureInfo.InvariantCulture).PadLeft(matches[0].Groups[2].Value.StartsWith('0') ? digits : 0, '0');
                    return Styled(prefix + text + suffix, position);
                });
            }

            return new Series(cells, null);
        }

        public Cell At(int position) => _generator is { } generator ? generator(position) : CopyAt(position);

        private Cell CopyAt(int position)
        {
            var cell = _cells[Modulo(position, _cells.Count)];
            return cell.IsSpilled ? Cell.Empty with { StyleId = cell.StyleId } : cell;
        }

        private static int Modulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;

        private static (double Slope, double Intercept) Trend(List<CellValue> values)
        {
            var count = values.Count;
            var meanX = (count - 1) / 2.0;
            var meanY = values.Average(value => value.Number);
            double sxy = 0, sxx = 0;
            for (var index = 0; index < count; index++)
            {
                sxy += (index - meanX) * (values[index].Number - meanY);
                sxx += (index - meanX) * (index - meanX);
            }

            var slope = sxx == 0 ? 0 : sxy / sxx;
            return (slope, meanY - slope * meanX);
        }

        /// <summary>Whole months between consecutive dates on the same day of month (1. Jan, 1. Feb …), or null.</summary>
        private static int? MonthStep(List<CellValue> values, bool date1904)
        {
            var dates = values.Select(value => DateSerial.ToDate(value.Number, date1904)).ToList();
            if (dates.Any(date => date.Day != dates[0].Day) || values.Any(value => value.Number != Math.Floor(value.Number)))
            {
                return null;
            }

            var step = (dates[1].Year - dates[0].Year) * 12 + dates[1].Month - dates[0].Month;
            for (var index = 2; index < dates.Count; index++)
            {
                if ((dates[index].Year - dates[index - 1].Year) * 12 + dates[index].Month - dates[index - 1].Month != step)
                {
                    return null;
                }
            }

            return step == 0 ? null : step;
        }

        /// <summary>Weekday or month names (full or abbreviated, German or English): the list, the first index and the step.</summary>
        private static (List<string> List, int Start, int Step)? NameList(List<CellValue> values, Globalization.CalcLocale locale)
        {
            var lists = new List<string[]>
            {
                Globalization.CalcLocale.German.DayNames, Globalization.CalcLocale.German.DayAbbreviations,
                Globalization.CalcLocale.German.MonthNames, Globalization.CalcLocale.German.MonthAbbreviations,
                Globalization.CalcLocale.English.DayNames, Globalization.CalcLocale.English.DayAbbreviations,
                Globalization.CalcLocale.English.MonthNames, Globalization.CalcLocale.English.MonthAbbreviations,
            };
            _ = locale;
            foreach (var list in lists)
            {
                var indices = values.Select(value => Array.FindIndex(list, name => string.Equals(name, value.Text, StringComparison.OrdinalIgnoreCase))).ToList();
                if (indices.Any(index => index < 0))
                {
                    continue;
                }

                var step = indices.Count > 1 ? indices[1] - indices[0] : 1;
                var casing = values[0].Text;
                var adjusted = list.Select(name => casing.All(char.IsUpper) ? name.ToUpper(CultureInfo.CurrentCulture) : name).ToList();
                return (adjusted, indices[0], step == 0 ? 1 : step);
            }

            return null;
        }
    }
}

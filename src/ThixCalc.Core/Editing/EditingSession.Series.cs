using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>Reihe (Start › Ausfüllen › Reihe): arithmetic, geometric, or by date units.</summary>
public enum SeriesType : byte
{
    Linear,
    Growth,
    Date,
}

/// <summary>The unit of a date series.</summary>
public enum SeriesDateUnit : byte
{
    Day,
    Weekday,
    Month,
    Year,
}

/// <summary>What the Reihe dialog asks: along rows or columns, the type, the date unit, the increment and an end value (null: the selection's end).</summary>
public sealed record SeriesOptions(bool InRows, SeriesType Type, double Step = 1, double? Stop = null, SeriesDateUnit DateUnit = SeriesDateUnit.Day);

public sealed partial class EditingSession
{
    /// <summary>
    /// Ausfüllen › Reihe: every row (or column) of the selection continues the number in its first cell – adding the increment,
    /// multiplying by it, or stepping dates by days, weekdays, months or years – up to the end value or the selection's end.
    /// With a single cell the series runs from it until the end value. One undo step.
    /// </summary>
    public void FillSeries(SeriesOptions options)
    {
        var sheet = ActiveSheet;
        var range = Selection;
        if (range.IsSingleCell && options.Stop is null)
        {
            throw new BusinessException("Geben Sie einen Endwert ein oder markieren Sie den Bereich, der gefüllt werden soll.", "stop");
        }

        var length = options.InRows ? range.ColumnCount : range.RowCount;
        if (range.IsSingleCell)
        {
            length = SheetLimits.MaxRows;
        }

        var lines = options.InRows ? range.RowCount : range.ColumnCount;
        if ((long)lines * Math.Min(length, 100_000) > 1_000_000)
        {
            throw new BusinessException("Die Auswahl ist für diesen Befehl zu groß.");
        }

        Execute("Reihe", () =>
        {
            for (var line = 0; line < lines; line++)
            {
                var first = options.InRows ? new CellAddress(range.FirstRow + line, range.FirstColumn) : new CellAddress(range.FirstRow, range.FirstColumn + line);
                var start = sheet.GetValue(first);
                if (!start.IsNumber)
                {
                    continue;
                }

                var style = sheet.GetStyleId(first.Row, first.Column);
                for (var index = 1; index < Math.Min(length, 100_000); index++)
                {
                    var value = Next(start.Number, index, options);
                    if (options.Stop is { } stop && (options.Step >= 0 ? value > stop + 1e-9 : value < stop - 1e-9))
                    {
                        break;
                    }

                    var cell = options.InRows ? new CellAddress(first.Row, first.Column + index) : new CellAddress(first.Row + index, first.Column);
                    if (cell.Row > SheetLimits.LastRow || cell.Column > SheetLimits.LastColumn)
                    {
                        break;
                    }

                    SetCell(sheet, cell.Row, cell.Column, new Cell(CellValue.FromNumber(value), null, style));
                }
            }
        });
    }

    private double Next(double start, int index, SeriesOptions options)
    {
        switch (options.Type)
        {
            case SeriesType.Growth:
                return start * Math.Pow(options.Step, index);
            case SeriesType.Date:
                var date1904 = Workbook.Date1904;
                if (DateSerial.ToDateTime(start, date1904) is not { } date)
                {
                    return start + index * options.Step;
                }

                var steps = (int)Math.Round(options.Step * index);
                var next = options.DateUnit switch
                {
                    SeriesDateUnit.Month => date.AddMonths(steps),
                    SeriesDateUnit.Year => date.AddYears(steps),
                    SeriesDateUnit.Weekday => AddWeekdays(date, steps),
                    _ => date.AddDays(steps),
                };
                return DateSerial.FromDateTime(next, date1904) + (start - Math.Floor(start));
            default:
                return start + index * options.Step;
        }
    }

    private static DateTime AddWeekdays(DateTime date, int days)
    {
        var direction = Math.Sign(days);
        var remaining = Math.Abs(days);
        while (remaining > 0)
        {
            date = date.AddDays(direction);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                remaining--;
            }
        }

        return date;
    }
}

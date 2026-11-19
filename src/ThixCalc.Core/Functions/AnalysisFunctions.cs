using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>
/// Grouping and forecasting: GRUPPIERENNACH (GROUPBY) and PIVOTMIT (PIVOTBY) – group rows by key columns and aggregate the
/// values with a LAMBDA or a function name (SUMME, MITTELWERT …), with totals, sorting and a filter – and the exponential
/// smoothing forecasts PROGNOSE.ETS(.KONFINT/.SAISONALITÄT/.STAT) (additive Holt-Winters, AAA, parameters fitted by least
/// squares; Excel does not document its optimizer, so results agree in trend and season, not to the last digit).
/// </summary>
internal static class AnalysisFunctions
{
    private const string TotalLabel = "Gesamtergebnis";

    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Lookup;
        r.Add("GROUPBY", 3, 8, GroupBy, [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Lambda, ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value], array: true);
        r.Add("PIVOTBY", 4, 11, PivotBy,
            [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Lambda, ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Array, ArgumentKind.Value, ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value], array: true);

        r.Add("GETPIVOTDATA", 2, 254, PivotData, [ArgumentKind.Value, ArgumentKind.Reference, ArgumentKind.Value], returnsReference: false);

        r.Category = FunctionCategory.Statistical;
        r.Add("FORECAST.ETS", 3, 6, call =>
        {
            var model = Fit(call, 1, 2, 3, 4, 5);
            return Result(model.Forecast(call.Number(0)));
        }, [ArgumentKind.Value, ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value]);
        r.Add("FORECAST.ETS.CONFINT", 3, 7, call =>
        {
            var level = call.Number(3, 0.95);
            Require(level > 0 && level < 1);
            var model = Fit(call, 1, 2, 4, 5, 6);
            return Result(model.Interval(call.Number(0), level));
        }, [ArgumentKind.Value, ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value]);
        r.Add("FORECAST.ETS.SEASONALITY", 2, 4, call => Result(Fit(call, 0, 1, 2, 3, -1, seasonOnly: true).Period),
            [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value]);
        r.Add("FORECAST.ETS.STAT", 3, 6, call =>
        {
            var model = Fit(call, 0, 1, 3, 4, 5);
            return Result(call.Integer(2) switch
            {
                1 => model.Alpha,
                2 => model.Beta,
                3 => model.Gamma,
                4 => model.Mase,
                5 => model.Smape,
                6 => model.Mae,
                7 => model.Rmse,
                8 => model.Step,
                _ => throw new CalcErrorException(CellError.Number),
            });
        }, [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value]);
    }

    /// <summary>PIVOTDATENZUORDNEN: a value of the PivotTable at the reference (filled in by the pivot model).</summary>
    private static Operand PivotData(FunctionCall call) => CellError.Reference;

    // ---------------------------------------------------------------- GROUPBY / PIVOTBY

    /// <summary>The rows of a key/value table after headers and filter: (key tuple, value tuple).</summary>
    private sealed record Table(IReadOnlyList<string> KeyNames, IReadOnlyList<string> ValueNames, List<(CellValue[] Keys, CellValue[] Values)> Rows, bool ShowHeaders);

    private static Table Read(FunctionCall call, ArrayValue keys, ArrayValue values, int headersIndex, int filterIndex, ArrayValue? second = null)
    {
        Require(keys.Rows == values.Rows && (second is null || second.Rows == keys.Rows), CellError.Value);
        var mode = call.Has(headersIndex) ? call.Integer(headersIndex) : -1;
        if (mode == -1)
        {
            // Automatic: a text in the first row over a number in the second is a header.
            mode = values.Rows > 1 && Enumerable.Range(0, values.Columns).All(column => values[0, column].IsText)
                && Enumerable.Range(0, values.Columns).Any(column => values[1, column].IsNumber) ? 3 : 0;
        }

        Require(mode is >= 0 and <= 3, CellError.Value);
        var hasHeaders = mode is 1 or 3;
        var keyNames = Enumerable.Range(0, keys.Columns).Select(column => hasHeaders ? Coercion.ToText(keys[0, column], call.Context.Locale) : $"Feld{column + 1}").ToList();
        var valueNames = Enumerable.Range(0, values.Columns).Select(column => hasHeaders ? Coercion.ToText(values[0, column], call.Context.Locale) : $"Wert{column + 1}").ToList();
        var filter = call.Has(filterIndex) ? call.Array(filterIndex) : null;
        var rows = new List<(CellValue[], CellValue[])>();
        for (var row = hasHeaders ? 1 : 0; row < keys.Rows; row++)
        {
            if (filter is not null)
            {
                var flag = filter.Broadcast(row, 0);
                if (flag.IsError)
                {
                    throw new CalcErrorException(flag.Error);
                }

                if (!Coercion.ToBoolean(flag, call.Context.Locale))
                {
                    continue;
                }
            }

            var keyRow = new CellValue[keys.Columns + (second?.Columns ?? 0)];
            for (var column = 0; column < keys.Columns; column++)
            {
                keyRow[column] = keys[row, column];
            }

            for (var column = 0; column < (second?.Columns ?? 0); column++)
            {
                keyRow[keys.Columns + column] = second![row, column];
            }

            var valueRow = new CellValue[values.Columns];
            for (var column = 0; column < values.Columns; column++)
            {
                valueRow[column] = values[row, column];
            }

            rows.Add((keyRow, valueRow));
        }

        return new Table(keyNames, valueNames, rows, mode is 2 or 3);
    }

    /// <summary>The group key of values: texts without case, numbers by value.</summary>
    private static string KeyOf(IEnumerable<CellValue> values) =>
        string.Join('\u0001', values.Select(value => value.Kind switch
        {
            CellValueKind.Text => "t" + value.Text.ToUpperInvariant(),
            CellValueKind.Number => "n" + value.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            CellValueKind.Boolean => "b" + value.Boolean,
            CellValueKind.Error => "e" + (int)value.Error,
            _ => "_",
        }));

    private static CellValue Aggregate(FunctionCall call, LambdaValue function, IReadOnlyList<CellValue> values)
    {
        if (values.Count == 0)
        {
            return CellValue.Blank;
        }

        var column = new ArrayValue(values.Count, 1, [.. values]);
        var result = Evaluator.CallLambda(function, [column], call.Context);
        result = Evaluator.Finish(result, call.Context);
        return result.Kind switch
        {
            OperandKind.Value => result.Value,
            OperandKind.Array when result.Array.Count == 1 => result.Array.Values[0],
            _ => CellError.Calc,
        };
    }

    private static int SortColumn(FunctionCall call, int index)
    {
        if (!call.Has(index))
        {
            return 1;
        }

        var order = call.Array(index).Values[0];
        var column = (int)Coercion.ToNumber(order, call.Context.Locale, call.Context.Date1904);
        Require(column != 0, CellError.Value);
        return column;
    }

    private static Operand GroupBy(FunctionCall call)
    {
        var keys = call.Array(0);
        var values = call.Array(1);
        var function = call.Lambda(2);
        var table = Read(call, keys, values, 3, 6);
        var depth = call.Integer(4, 1);
        Require(depth is >= -2 and <= 2, CellError.Value);
        var sortColumn = SortColumn(call, 5);
        var keyCount = keys.Columns;
        var valueCount = values.Columns;
        Require(Math.Abs(sortColumn) <= keyCount + valueCount, CellError.Value);

        var groups = table.Rows.GroupBy(row => KeyOf(row.Keys)).Select(group => (Keys: group.First().Keys, Rows: group.ToList())).ToList();
        var lines = groups.Select(group =>
        {
            var line = new CellValue[keyCount + valueCount];
            Array.Copy(group.Keys, line, keyCount);
            for (var column = 0; column < valueCount; column++)
            {
                line[keyCount + column] = Aggregate(call, function, group.Rows.Select(row => row.Values[column]).ToList());
            }

            return line;
        }).ToList();
        var index = Math.Abs(sortColumn) - 1;
        lines.Sort((left, right) =>
        {
            var compared = ArrayFunctions.CompareForSort(left[index], right[index]);
            for (var key = 0; compared == 0 && key < keyCount; key++)
            {
                compared = ArrayFunctions.CompareForSort(left[key], right[key]);
            }

            return sortColumn < 0 ? -compared : compared;
        });

        // Subtotals per first key (depth 2) and the grand total (depth 1 or 2; negative = on top).
        if (Math.Abs(depth) == 2 && keyCount > 1)
        {
            var withSubtotals = new List<CellValue[]>();
            foreach (var block in lines.GroupBy(line => KeyOf([line[0]])).ToList())
            {
                var subtotal = new CellValue[keyCount + valueCount];
                subtotal[0] = block.First()[0];
                var members = table.Rows.Where(row => KeyOf([row.Keys[0]]) == block.Key).ToList();
                for (var column = 0; column < valueCount; column++)
                {
                    subtotal[keyCount + column] = Aggregate(call, function, members.Select(row => row.Values[column]).ToList());
                }

                if (depth < 0)
                {
                    withSubtotals.Add(subtotal);
                }

                withSubtotals.AddRange(block);
                if (depth > 0)
                {
                    withSubtotals.Add(subtotal);
                }
            }

            lines = withSubtotals;
        }

        if (depth != 0)
        {
            var total = new CellValue[keyCount + valueCount];
            total[0] = TotalLabel;
            for (var column = 0; column < valueCount; column++)
            {
                total[keyCount + column] = Aggregate(call, function, table.Rows.Select(row => row.Values[column]).ToList());
            }

            if (depth < 0)
            {
                lines.Insert(0, total);
            }
            else
            {
                lines.Add(total);
            }
        }

        if (table.ShowHeaders)
        {
            lines.Insert(0, [.. table.KeyNames.Select(name => (CellValue)name), .. table.ValueNames.Select(name => (CellValue)name)]);
        }

        return ToArray(lines, keyCount + valueCount);
    }

    private static Operand PivotBy(FunctionCall call)
    {
        var rowKeys = call.Array(0);
        var columnKeys = call.Array(1);
        var values = call.Array(2);
        var function = call.Lambda(3);
        Require(columnKeys.Rows == rowKeys.Rows, CellError.Value);
        var table = Read(call, rowKeys, values, 4, 9, columnKeys);
        var rowDepth = call.Integer(5, 1);
        var rowSort = SortColumn(call, 6);
        var columnDepth = call.Integer(7, 1);
        var columnSort = SortColumn(call, 8);
        var keyCount = rowKeys.Columns;
        var columnKeyCount = columnKeys.Columns;
        var valueCount = values.Columns;

        CellValue[] RowKey((CellValue[] Keys, CellValue[] Values) row) => row.Keys[..keyCount];
        CellValue[] ColumnKey((CellValue[] Keys, CellValue[] Values) row) => row.Keys[keyCount..];

        var rowGroups = table.Rows.GroupBy(row => KeyOf(RowKey(row))).Select(group => RowKey(group.First())).ToList();
        var columnGroups = table.Rows.GroupBy(row => KeyOf(ColumnKey(row))).Select(group => ColumnKey(group.First())).ToList();
        int Compare(CellValue[] left, CellValue[] right)
        {
            for (var index = 0; index < left.Length; index++)
            {
                var compared = ArrayFunctions.CompareForSort(left[index], right[index]);
                if (compared != 0)
                {
                    return compared;
                }
            }

            return 0;
        }

        rowGroups.Sort((left, right) => rowSort < 0 ? -Compare(left, right) : Compare(left, right));
        columnGroups.Sort((left, right) => columnSort < 0 ? -Compare(left, right) : Compare(left, right));

        var cells = new Dictionary<(string, string), List<(CellValue[] Keys, CellValue[] Values)>>();
        foreach (var row in table.Rows)
        {
            var key = (KeyOf(RowKey(row)), KeyOf(ColumnKey(row)));
            if (!cells.TryGetValue(key, out var list))
            {
                cells[key] = list = [];
            }

            list.Add(row);
        }

        // Columns: per column group (and value field), then the totals.
        var columnCount = keyCount + (columnGroups.Count + (columnDepth != 0 ? 1 : 0)) * valueCount;
        var lines = new List<CellValue[]>();
        var header = new CellValue[columnCount];
        var position = keyCount;
        foreach (var group in columnGroups)
        {
            for (var value = 0; value < valueCount; value++)
            {
                var label = string.Join(" - ", group.Select(key => Coercion.ToText(key, call.Context.Locale)));
                header[position++] = valueCount > 1 ? $"{label} - {table.ValueNames[value]}" : label;
            }
        }

        for (var value = 0; columnDepth != 0 && value < valueCount; value++)
        {
            header[position++] = valueCount > 1 ? $"{TotalLabel} - {table.ValueNames[value]}" : TotalLabel;
        }

        if (table.ShowHeaders)
        {
            for (var key = 0; key < keyCount; key++)
            {
                header[key] = table.KeyNames[key];
            }
        }

        lines.Add(header);
        IEnumerable<(CellValue[] Keys, CellValue[] Values)> Rows(string? rowKey, string? columnKey) => table.Rows.Where(row =>
            (rowKey is null || KeyOf(RowKey(row)) == rowKey) && (columnKey is null || KeyOf(ColumnKey(row)) == columnKey));

        CellValue[] Line(CellValue[]? keys, string? rowKey)
        {
            var line = new CellValue[columnCount];
            if (keys is null)
            {
                line[0] = TotalLabel;
            }
            else
            {
                Array.Copy(keys, line, keyCount);
            }

            var at = keyCount;
            foreach (var group in columnGroups)
            {
                var columnKey = KeyOf(group);
                var members = rowKey is null ? Rows(null, columnKey).ToList() : cells.GetValueOrDefault((rowKey, columnKey)) ?? [];
                for (var value = 0; value < valueCount; value++)
                {
                    line[at++] = Aggregate(call, function, members.Select(row => row.Values[value]).ToList());
                }
            }

            for (var value = 0; columnDepth != 0 && value < valueCount; value++)
            {
                line[at++] = Aggregate(call, function, Rows(rowKey, null).Select(row => row.Values[value]).ToList());
            }

            return line;
        }

        foreach (var keys in rowGroups)
        {
            lines.Add(Line(keys, KeyOf(keys)));
        }

        if (rowDepth != 0)
        {
            lines.Insert(rowDepth < 0 ? 1 : lines.Count, Line(null, null));
        }

        return ToArray(lines, columnCount);
    }

    private static Operand ToArray(List<CellValue[]> lines, int columns)
    {
        if (lines.Count == 0 || columns == 0)
        {
            return CellError.Calc;
        }

        // Gaps (no rows for a combination, the corner above the keys) stay empty, not 0.
        foreach (var line in lines)
        {
            for (var column = 0; column < line.Length; column++)
            {
                if (line[column].IsBlank)
                {
                    line[column] = CellValue.EmptyText;
                }
            }
        }

        var result = new ArrayValue(lines.Count, columns);
        for (var row = 0; row < lines.Count; row++)
        {
            Array.Copy(lines[row], 0, result.Values, row * columns, columns);
        }

        return result;
    }

    // ---------------------------------------------------------------- FORECAST.ETS

    private static Smoothing Fit(FunctionCall call, int valuesIndex, int timelineIndex, int seasonalityIndex, int completionIndex, int aggregationIndex, bool seasonOnly = false)
    {
        var values = call.Array(valuesIndex).Values;
        var timeline = call.Array(timelineIndex).Values;
        Require(values.Length == timeline.Length && values.Length >= 3, CellError.Value);
        var seasonality = seasonalityIndex >= 0 ? call.Integer(seasonalityIndex, 1) : 1;
        Require(seasonality is >= 0 and <= 8760);
        var completion = completionIndex >= 0 ? call.Integer(completionIndex, 1) : 1;
        var aggregation = aggregationIndex >= 0 ? call.Integer(aggregationIndex, 1) : 1;
        Require(completion is 0 or 1 && aggregation is >= 1 and <= 7);

        // Duplicates of a time are aggregated, the timeline sorted and made regular.
        var points = new SortedDictionary<double, List<double>>();
        for (var index = 0; index < values.Length; index++)
        {
            Require(timeline[index].IsNumber, CellError.Value);
            if (!points.TryGetValue(timeline[index].Number, out var list))
            {
                points[timeline[index].Number] = list = [];
            }

            if (values[index].IsNumber)
            {
                list.Add(values[index].Number);
            }
        }

        var times = points.Keys.ToList();
        var step = Enumerable.Range(1, times.Count - 1).Select(index => times[index] - times[index - 1]).Where(gap => gap > 0).DefaultIfEmpty(0).Min();
        Require(step > 0, CellError.Number);
        var count = (int)Math.Round((times[^1] - times[0]) / step) + 1;
        Require(count <= 100_000);
        var series = new double?[count];
        foreach (var (time, list) in points)
        {
            if (list.Count > 0)
            {
                series[(int)Math.Round((time - times[0]) / step)] = Aggregated(list, aggregation);
            }
        }

        var filled = Fill(series, completion);
        var period = seasonality == 1 ? DetectPeriod(filled) : seasonality;
        return Smoothing.Fit(filled, period, times[0], step, seasonOnly);
    }

    private static double Aggregated(List<double> values, int aggregation) => aggregation switch
    {
        1 => values.Average(),
        2 => values.Count,
        3 => values.Count,
        4 => values.Max(),
        5 => values.OrderBy(value => value).ElementAt(values.Count / 2) is var middle && values.Count % 2 == 0
            ? (values.OrderBy(value => value).ElementAt(values.Count / 2 - 1) + middle) / 2 : middle,
        6 => values.Min(),
        _ => values.Sum(),
    };

    private static double[] Fill(double?[] series, int completion)
    {
        var result = new double[series.Length];
        for (var index = 0; index < series.Length; index++)
        {
            if (series[index] is { } value)
            {
                result[index] = value;
                continue;
            }

            if (completion == 0)
            {
                result[index] = 0;
                continue;
            }

            var before = index - 1;
            var after = Array.FindIndex(series, index + 1, value => value is not null);
            result[index] = before >= 0 && after > 0
                ? result[before] + (series[after]!.Value - result[before]) * (index - before) / (after - before)
                : before >= 0 ? result[before] : series[after]!.Value;
        }

        return result;
    }

    /// <summary>The season length with the strongest autocorrelation of the detrended series (0 = none).</summary>
    private static int DetectPeriod(double[] series)
    {
        var count = series.Length;
        if (count < 6)
        {
            return 0;
        }

        // Detrend with a least squares line.
        var meanX = (count - 1) / 2.0;
        var meanY = series.Average();
        var slope = Enumerable.Range(0, count).Sum(index => (index - meanX) * (series[index] - meanY)) / Enumerable.Range(0, count).Sum(index => (index - meanX) * (index - meanX));
        var residuals = Enumerable.Range(0, count).Select(index => series[index] - meanY - slope * (index - meanX)).ToArray();
        var variance = residuals.Sum(value => value * value);
        if (variance <= 1e-12)
        {
            return 0;
        }

        var best = 0;
        var bestCorrelation = 0.3;
        for (var lag = 2; lag <= count / 2; lag++)
        {
            var correlation = 0.0;
            for (var index = lag; index < count; index++)
            {
                correlation += residuals[index] * residuals[index - lag];
            }

            correlation /= variance;
            if (correlation > bestCorrelation + 0.02)
            {
                best = lag;
                bestCorrelation = correlation;
            }
        }

        return best;
    }

    /// <summary>Additive Holt-Winters (level, trend, season) with α, β, γ fitted by minimizing the one-step errors.</summary>
    private sealed class Smoothing
    {
        private double[] _season = [];
        private double _level;
        private double _trend;

        public int Period { get; private init; }

        public double Alpha { get; private set; }

        public double Beta { get; private set; }

        public double Gamma { get; private set; }

        public double Step { get; private init; }

        public double Rmse { get; private set; }

        public double Mae { get; private set; }

        public double Smape { get; private set; }

        public double Mase { get; private set; }

        private double Start { get; init; }

        private int Count { get; init; }

        public static Smoothing Fit(double[] series, int period, double start, double step, bool seasonOnly)
        {
            if (period > series.Length / 2)
            {
                period = 0;
            }

            var model = new Smoothing { Period = period, Start = start, Step = step, Count = series.Length };
            if (seasonOnly)
            {
                return model;
            }

            var best = double.MaxValue;
            (double, double, double) chosen = (0.5, 0.1, 0.1);
            double[] grid = [0.001, 0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.999];
            foreach (var alpha in grid)
            {
                foreach (var beta in grid)
                {
                    foreach (var gamma in period > 0 ? grid : [0.0])
                    {
                        var error = model.Run(series, alpha, beta, gamma).Squares;
                        if (error < best)
                        {
                            best = error;
                            chosen = (alpha, beta, gamma);
                        }
                    }
                }
            }

            // Refine around the best point.
            for (var size = 0.04; size > 0.0005; size /= 2)
            {
                var improved = true;
                while (improved)
                {
                    improved = false;
                    foreach (var (da, db, dg) in new[] { (size, 0.0, 0.0), (-size, 0.0, 0.0), (0.0, size, 0.0), (0.0, -size, 0.0), (0.0, 0.0, size), (0.0, 0.0, -size) })
                    {
                        var candidate = (Clamp(chosen.Item1 + da), Clamp(chosen.Item2 + db), period > 0 ? Clamp(chosen.Item3 + dg) : 0);
                        var error = model.Run(series, candidate.Item1, candidate.Item2, candidate.Item3).Squares;
                        if (error < best - 1e-12)
                        {
                            best = error;
                            chosen = candidate;
                            improved = true;
                        }
                    }
                }
            }

            (model.Alpha, model.Beta, model.Gamma) = chosen;
            var run = model.Run(series, model.Alpha, model.Beta, model.Gamma, keep: true);
            var errors = run.Errors;
            model.Rmse = Math.Sqrt(errors.Average(error => error.Error * error.Error));
            model.Mae = errors.Average(error => Math.Abs(error.Error));
            model.Smape = errors.Average(error => Math.Abs(error.Actual) + Math.Abs(error.Actual - error.Error) is var sum && sum > 0 ? 2 * Math.Abs(error.Error) / sum : 0);
            var naive = Enumerable.Range(1, series.Length - 1).Average(index => Math.Abs(series[index] - series[index - 1]));
            model.Mase = naive > 0 ? model.Mae / naive : 0;
            return model;
        }

        private static double Clamp(double value) => Math.Clamp(value, 0.001, 0.999);

        private (double Squares, List<(double Actual, double Error)> Errors) Run(double[] series, double alpha, double beta, double gamma, bool keep = false)
        {
            var period = Period;
            var level = series[0];
            var trend = series.Length > 1 ? series[1] - series[0] : 0;
            var season = new double[Math.Max(period, 1)];
            var begin = 1;
            if (period > 0)
            {
                var first = series.Take(period).Average();
                var second = series.Length >= 2 * period ? series.Skip(period).Take(period).Average() : first;
                level = first;
                trend = (second - first) / period;
                for (var index = 0; index < period; index++)
                {
                    season[index] = series[index] - first;
                }

                begin = period;
            }

            var squares = 0.0;
            var errors = new List<(double, double)>();
            for (var index = begin; index < series.Length; index++)
            {
                var seasonal = period > 0 ? season[index % period] : 0;
                var forecast = level + trend + seasonal;
                var error = series[index] - forecast;
                squares += error * error;
                if (keep)
                {
                    errors.Add((series[index], error));
                }

                var previous = level;
                level = alpha * (series[index] - seasonal) + (1 - alpha) * (level + trend);
                trend = beta * (level - previous) + (1 - beta) * trend;
                if (period > 0)
                {
                    season[index % period] = gamma * (series[index] - level) + (1 - gamma) * seasonal;
                }
            }

            if (keep)
            {
                _level = level;
                _trend = trend;
                _season = season;
            }

            return (squares, errors);
        }

        /// <summary>The forecast at a time after the timeline (fractional steps are interpolated).</summary>
        public double Forecast(double time)
        {
            var ahead = (time - Start) / Step - (Count - 1);
            Require(ahead > 0, CellError.Number);
            var seasonal = 0.0;
            if (Period > 0)
            {
                var position = Count - 1 + ahead;
                var lower = (int)Math.Floor(position);
                var fraction = position - lower;
                seasonal = _season[lower % Period] * (1 - fraction) + _season[(lower + 1) % Period] * fraction;
            }

            return _level + ahead * _trend + seasonal;
        }

        /// <summary>Half the width of the prediction interval at <paramref name="level"/> confidence.</summary>
        public double Interval(double time, double level)
        {
            var ahead = Math.Max(1, (time - Start) / Step - (Count - 1));
            var variance = 1.0;
            for (var step = 1; step < ahead; step++)
            {
                var weight = Alpha * (1 + step * Beta) + (Period > 0 && step % Period == 0 ? Gamma * (1 - Alpha) : 0);
                variance += weight * weight;
            }

            return SpecialFunctions.NormalInverse((1 + level) / 2) * Rmse * Math.Sqrt(variance);
        }
    }
}

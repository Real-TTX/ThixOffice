using System.Text;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Formats.Csv;

/// <summary>How a CSV file is read or written.</summary>
public sealed record CsvOptions
{
    /// <summary>The field separator; null detects it (; , tab |).</summary>
    public char? Delimiter { get; init; }

    /// <summary>The encoding; null detects it (BOM, valid UTF-8, else Windows-1252).</summary>
    public Encoding? Encoding { get; init; }

    /// <summary>The locale values are recognized and written in (German: decimal comma, ";" as separator).</summary>
    public CalcLocale Locale { get; init; } = CalcLocale.German;

    /// <summary>Recognize numbers, dates, booleans (Excel's behaviour); false keeps every field as text.</summary>
    public bool DetectTypes { get; init; } = true;

    /// <summary>Write a UTF-8 byte order mark (Excel needs it to read UTF-8 CSV correctly).</summary>
    public bool WriteByteOrderMark { get; init; } = true;
}

/// <summary>
/// CSV and TSV: reading with delimiter and encoding detection, RFC 4180 quoting (quoted fields with separators, quotes and
/// line breaks), type recognition in the locale (1.234,56 and 31.12.2024 in German); writing the displayed values of a sheet
/// with the locale's separator (";" in German, as German Excel does), quoting where needed.
/// </summary>
public static class CsvFormat
{
    private static readonly char[] Candidates = [';', ',', '\t', '|'];

    public static Workbook Read(Stream stream, CsvOptions? options = null, string sheetName = "Tabelle1")
    {
        options ??= new CsvOptions();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var (encoding, offset) = DetectEncoding(bytes, options.Encoding);
        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        var delimiter = options.Delimiter ?? DetectDelimiter(text, options.Locale);

        var workbook = Workbook.Create(sheetName);
        workbook.Calculation.Locale = options.Locale;
        var sheet = workbook.Sheets[0];
        var row = 0;
        foreach (var fields in Parse(text, delimiter))
        {
            for (var column = 0; column < fields.Count && column < SheetLimits.MaxColumns; column++)
            {
                var field = fields[column].Text;
                if (field.Length == 0)
                {
                    continue;
                }

                // Excel recognizes values in quoted fields too; quotes only protect separators and line breaks.
                var parsed = options.DetectTypes ? ValueParser.ParseInput(field, options.Locale) : null;
                var value = parsed?.Kind is InputKind.Number or InputKind.Boolean or InputKind.Error ? parsed.Value : CellValue.FromText(field);
                var style = parsed?.NumberFormat is { } code && value.IsNumber ? workbook.Styles.Intern(CellStyle.Default with { NumberFormat = code }) : 0;
                sheet.LoadValue(row, column, value, style);
            }

            row++;
            if (row >= SheetLimits.MaxRows)
            {
                break;
            }
        }

        workbook.Calculation.Rebuild(markDirty: false);
        workbook.ForgetChanges();
        return workbook;
    }

    /// <summary>Writes the sheet's displayed values (number formats applied, as Excel's CSV export does).</summary>
    public static void Write(Worksheet sheet, Stream stream, CsvOptions? options = null)
    {
        options ??= new CsvOptions();
        var delimiter = options.Delimiter ?? options.Locale.ListSeparator;
        var encoding = options.Encoding ?? new UTF8Encoding(options.WriteByteOrderMark);
        using var writer = new StreamWriter(stream, encoding, leaveOpen: true) { NewLine = "\r\n" };
        if (sheet.DataRange is not { } range)
        {
            return;
        }

        var workbook = sheet.Workbook;
        for (var row = 0; row <= range.LastRow; row++)
        {
            var line = new StringBuilder();
            for (var column = 0; column <= range.LastColumn; column++)
            {
                if (column > 0)
                {
                    line.Append(delimiter);
                }

                var value = sheet.GetValue(row, column);
                if (value.IsBlank)
                {
                    continue;
                }

                var text = NumberFormatter.Format(value, sheet.GetStyle(row, column).NumberFormat, options.Locale, workbook.Date1904).Text;
                line.Append(Quote(text, delimiter));
            }

            writer.WriteLine(line.ToString().TrimEnd(delimiter));
        }
    }

    private static string Quote(string text, char delimiter)
    {
        if (text.IndexOfAny([delimiter, '"', '\n', '\r']) < 0 && !text.StartsWith(' ') && !text.EndsWith(' '))
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    internal static (Encoding Encoding, int Offset) DetectEncoding(byte[] bytes, Encoding? requested)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(false), 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode, 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode, 2);
        }

        if (requested is not null)
        {
            return (requested, 0);
        }

        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(bytes);
            return (new UTF8Encoding(false), 0);
        }
        catch (DecoderFallbackException)
        {
            return (Latin1Windows, 0);
        }
    }

    /// <summary>Windows-1252 (the encoding German Excel writes CSV in without "UTF-8").</summary>
    private static Encoding Latin1Windows
    {
        get
        {
            try
            {
                return CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;
            }
            catch (ArgumentException)
            {
                return Encoding.Latin1;
            }
        }
    }

    /// <summary>The separator that splits the first lines into the most consistent number of fields (ties: the locale's).</summary>
    internal static char DetectDelimiter(string text, CalcLocale locale)
    {
        var lines = Parse(text, '\u0001').Take(20).Select(fields => fields.Count > 0 ? fields[0].Text : string.Empty).ToList();
        var sample = string.Join("\n", lines);
        var best = locale.ListSeparator;
        var bestScore = 0.0;
        foreach (var candidate in Candidates)
        {
            var counts = Parse(sample, candidate).Take(20).Select(fields => fields.Count).ToList();
            if (counts.Count == 0 || counts.Max() <= 1)
            {
                continue;
            }

            var mode = counts.GroupBy(count => count).OrderByDescending(group => group.Count()).First();
            var score = mode.Key * (double)mode.Count() / counts.Count + (candidate == locale.ListSeparator ? 0.01 : 0);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Splits CSV text into records of fields (RFC 4180 quoting; \r\n, \n or \r line ends).</summary>
    internal static IEnumerable<List<(string Text, bool Quoted)>> Parse(string text, char delimiter)
    {
        var fields = new List<(string, bool)>();
        var field = new StringBuilder();
        var quoted = false;
        var inQuotes = false;
        var index = 0;
        var any = false;
        while (index < text.Length)
        {
            var character = text[index];
            any = true;
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index += 2;
                        continue;
                    }

                    inQuotes = false;
                    index++;
                    continue;
                }

                field.Append(character);
                index++;
                continue;
            }

            if (character == '"' && field.Length == 0 && !quoted)
            {
                inQuotes = true;
                quoted = true;
                index++;
                continue;
            }

            if (character == delimiter)
            {
                fields.Add((field.ToString(), quoted));
                field.Clear();
                quoted = false;
                index++;
                continue;
            }

            if (character is '\r' or '\n')
            {
                fields.Add((field.ToString(), quoted));
                field.Clear();
                quoted = false;
                yield return fields;
                fields = [];
                any = false;
                index += character == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                continue;
            }

            field.Append(character);
            index++;
        }

        if (any || field.Length > 0 || fields.Count > 0)
        {
            fields.Add((field.ToString(), quoted));
            yield return fields;
        }
    }
}

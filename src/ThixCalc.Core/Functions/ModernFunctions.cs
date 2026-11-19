using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>
/// The rest of Excel's library: regular expressions (REGEXTEST, REGEXEXTRACT, REGEXREPLACE), the East Asian text
/// conversions (ASC, JIS/DBCS, PHONETIC, BAHTTEXT), INFO, XMLFILTERN (offline, from a text), WEBDIENST (no network: #WERT!),
/// PROZENTVON and GESTUTZTBEREICH.
/// </summary>
internal static class ModernFunctions
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Text;
        r.Add("REGEXTEST", 2, 3, call => Pattern(call, 1, 2).IsMatch(call.Text(0)));
        r.Add("REGEXEXTRACT", 2, 4, RegexExtract);
        r.Add("REGEXREPLACE", 3, 5, RegexReplace);
        r.Add("ASC", 1, 1, call => ToHalfWidth(call.Text(0)));
        r.Add("JIS", 1, 1, call => ToFullWidth(call.Text(0)));
        r.Add("DBCS", 1, 1, call => ToFullWidth(call.Text(0)));
        r.Add("PHONETIC", 1, 1, call => call.Text(0), [ArgumentKind.Value]);
        r.Add("BAHTTEXT", 1, 1, call => BahtText(call.Number(0)));

        r.Category = FunctionCategory.Information;
        r.Add("INFO", 1, 1, Info, isVolatile: true);

        r.Category = FunctionCategory.Web;
        r.Add("FILTERXML", 2, 2, FilterXml);
        r.Add("WEBSERVICE", 1, 1, _ => CellError.Value);

        r.Category = FunctionCategory.Math;
        r.Add("PERCENTOF", 2, 2, call =>
        {
            var part = call.Values(0).Where(value => value.IsNumber).Sum(value => value.Number);
            var all = call.Values(1).Where(value => value.IsNumber).Sum(value => value.Number);
            Require(all != 0, CellError.DivisionByZero);
            return Result(part / all);
        }, [ArgumentKind.Any]);

        r.Category = FunctionCategory.Lookup;
        r.Add("TRIMRANGE", 1, 3, TrimRange, [ArgumentKind.Reference, ArgumentKind.Value], returnsReference: true);
    }

    // ---------------------------------------------------------------- regular expressions

    private static Regex Pattern(FunctionCall call, int index, int caseIndex)
    {
        var options = call.Integer(caseIndex, 0) switch
        {
            0 => RegexOptions.None,
            1 => RegexOptions.IgnoreCase,
            _ => throw new CalcErrorException(CellError.Value),
        };
        try
        {
            return new Regex(call.Text(index), options | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            throw new CalcErrorException(CellError.Value);
        }
    }

    private static Operand RegexExtract(FunctionCall call)
    {
        var text = call.Text(0);
        var pattern = Pattern(call, 1, 3);
        switch (call.Integer(2, 0))
        {
            case 0:
                var match = pattern.Match(text);
                return match.Success ? match.Value : CellError.NotAvailable;
            case 1:
                var all = pattern.Matches(text);
                if (all.Count == 0)
                {
                    return CellError.NotAvailable;
                }

                var list = new ArrayValue(all.Count, 1);
                for (var index = 0; index < all.Count; index++)
                {
                    list.Values[index] = all[index].Value;
                }

                return list;
            case 2:
                var first = pattern.Match(text);
                if (!first.Success)
                {
                    return CellError.NotAvailable;
                }

                if (first.Groups.Count <= 1)
                {
                    return first.Value;
                }

                var groups = new ArrayValue(1, first.Groups.Count - 1);
                for (var index = 1; index < first.Groups.Count; index++)
                {
                    groups.Values[index - 1] = first.Groups[index].Value;
                }

                return groups;
            default:
                return CellError.Value;
        }
    }

    private static Operand RegexReplace(FunctionCall call)
    {
        var text = call.Text(0);
        var pattern = Pattern(call, 1, 4);
        var replacement = call.Text(2);
        var occurrence = call.Integer(3, 0);
        if (occurrence == 0)
        {
            return pattern.Replace(text, replacement);
        }

        var matches = pattern.Matches(text);
        var index = occurrence > 0 ? occurrence - 1 : matches.Count + occurrence;
        if (index < 0 || index >= matches.Count)
        {
            return text;
        }

        var match = matches[index];
        return text[..match.Index] + match.Result(replacement) + text[(match.Index + match.Length)..];
    }

    // ---------------------------------------------------------------- East Asian text

    /// <summary>ASC: full-width (zenkaku) letters, digits, signs and katakana to half-width.</summary>
    internal static string ToHalfWidth(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(character switch
            {
                >= '！' and <= '～' => ((char)(character - 0xFEE0)).ToString(),
                '　' => " ",
                _ => character.ToString(),
            });
        }

        return builder.ToString();
    }

    /// <summary>JIS/DBCS: half-width letters, digits, signs and katakana to full-width.</summary>
    internal static string ToFullWidth(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is >= '!' and <= '~')
            {
                builder.Append((char)(character + 0xFEE0));
            }
            else if (character == ' ')
            {
                builder.Append('　');
            }
            else if (character is >= '｡' and <= 'ﾟ')
            {
                // Half-width katakana (with a following voicing mark) become their full-width form.
                var length = index + 1 < text.Length && text[index + 1] is 'ﾞ' or 'ﾟ' ? 2 : 1;
                builder.Append(text.Substring(index, length).Normalize(NormalizationForm.FormKC));
                index += length - 1;
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static readonly string[] ThaiDigits = ["", "หนึ่ง", "สอง", "สาม", "สี่", "ห้า", "หก", "เจ็ด", "แปด", "เก้า"];
    private static readonly string[] ThaiPlaces = ["", "สิบ", "ร้อย", "พัน", "หมื่น", "แสน"];

    /// <summary>BAHTTEXT: a number as Thai text with บาท (baht) and สตางค์ (satang), ถ้วน when there are no satang.</summary>
    internal static string BahtText(double number)
    {
        var rounded = Math.Round(Math.Abs(number), 2, MidpointRounding.AwayFromZero);
        var baht = Math.Floor(rounded);
        var satang = (long)Math.Round((rounded - baht) * 100);
        var builder = new StringBuilder();
        if (number < 0 && rounded != 0)
        {
            builder.Append("ลบ");
        }

        if (baht > 0)
        {
            builder.Append(ThaiNumber((long)baht)).Append("บาท");
        }

        if (satang > 0)
        {
            builder.Append(ThaiNumber(satang)).Append("สตางค์");
        }
        else
        {
            builder.Append(baht > 0 ? "ถ้วน" : "ศูนย์บาทถ้วน");
        }

        return builder.ToString();
    }

    private static string ThaiNumber(long number)
    {
        if (number >= 1_000_000)
        {
            return ThaiNumber(number / 1_000_000) + "ล้าน" + (number % 1_000_000 > 0 ? ThaiNumber(number % 1_000_000) : string.Empty);
        }

        var digits = number.ToString(CultureInfo.InvariantCulture);
        var builder = new StringBuilder();
        for (var index = 0; index < digits.Length; index++)
        {
            var digit = digits[index] - '0';
            var place = digits.Length - 1 - index;
            if (digit == 0)
            {
                continue;
            }

            if (place == 1 && digit == 1)
            {
                builder.Append("สิบ");
            }
            else if (place == 1 && digit == 2)
            {
                builder.Append("ยี่สิบ");
            }
            else if (place == 0 && digit == 1 && digits.Length > 1)
            {
                builder.Append("เอ็ด");
            }
            else
            {
                builder.Append(ThaiDigits[digit]).Append(ThaiPlaces[place]);
            }
        }

        return builder.ToString();
    }

    // ---------------------------------------------------------------- information, web

    private static Operand Info(FunctionCall call) => call.Text(0).ToLowerInvariant() switch
    {
        "directory" => Environment.CurrentDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
        "numfile" => call.Context.Workbook.Sheets.Count,
        "origin" => "$A:$A$1",
        "osversion" => OperatingSystem.IsWindows() ? $"Windows (64-bit) NT {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor:00}" : $"Macintosh (64-bit) {Environment.OSVersion.Version}",
        "recalc" => call.Context.Workbook.CalculationSettings.Manual ? "Manuell" : "Automatisch",
        "release" => "16.0",
        "system" => OperatingSystem.IsMacOS() ? "mac" : "pcdos",
        _ => CellError.Value,
    };

    /// <summary>XMLFILTERN: the values an XPath selects in an XML text (numbers as numbers), several as a column.</summary>
    private static Operand FilterXml(FunctionCall call)
    {
        XmlNodeList? nodes;
        try
        {
            var document = new XmlDocument { XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(call.Text(0)), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
            nodes = document.SelectNodes(call.Text(1));
        }
        catch (Exception exception) when (exception is XmlException or System.Xml.XPath.XPathException)
        {
            return CellError.Value;
        }

        if (nodes is null || nodes.Count == 0)
        {
            return CellError.Value;
        }

        var result = new ArrayValue(nodes.Count, 1);
        for (var index = 0; index < nodes.Count; index++)
        {
            var text = nodes[index]!.InnerText;
            result.Values[index] = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : text;
        }

        return result.Count == 1 ? result.Values[0] : result;
    }

    // ---------------------------------------------------------------- TRIMRANGE

    /// <summary>GESTUTZTBEREICH: the reference without its empty outer rows and columns (1 leading, 2 trailing, 3 both – the default).</summary>
    private static Operand TrimRange(FunctionCall call)
    {
        var reference = call.Reference(0) ?? throw new CalcErrorException(CellError.Value);
        Require(reference.IsSingleArea, CellError.Value);
        var rows = call.Integer(1, 3);
        var columns = call.Integer(2, 3);
        Require(rows is >= 0 and <= 3 && columns is >= 0 and <= 3, CellError.Value);
        var area = reference.First;
        var range = area.Range;
        CellRange? used = null;
        foreach (var entry in area.Sheet.GetCells(range))
        {
            if (area.Sheet.HasContent(entry.Row, entry.Column))
            {
                var cell = CellRange.Cell(entry.Row, entry.Column);
                used = used is { } existing ? existing.Union(cell) : cell;
            }
        }

        if (used is not { } content)
        {
            return CellError.Reference;
        }

        var firstRow = (rows & 1) != 0 ? content.FirstRow : range.FirstRow;
        var lastRow = (rows & 2) != 0 ? content.LastRow : range.LastRow;
        var firstColumn = (columns & 1) != 0 ? content.FirstColumn : range.FirstColumn;
        var lastColumn = (columns & 2) != 0 ? content.LastColumn : range.LastColumn;
        return new ReferenceValue(area.Sheet, new CellRange(firstRow, firstColumn, lastRow, lastColumn));
    }
}

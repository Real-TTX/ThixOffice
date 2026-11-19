using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using A = DocumentFormat.OpenXml.Drawing;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace ThixCalc.Core.Formats.Xlsx;

/// <summary>
/// Reads .xlsx/.xlsm into a <see cref="Workbook"/>: sheets, cells with shared strings, shared and array/dynamic-array
/// formulas with their cached values (no recalculation on load), styles, defined names, rows/columns, merges, views,
/// validation, conditional formats, autofilter, hyperlinks, notes and tables. Worksheets are read with the SDK's streaming
/// reader. The original package is kept: <see cref="XlsxWriter"/> writes into it, so charts, pictures, pivot tables, macros
/// and everything else the model does not own survive; <see cref="Workbook.UnsupportedContent"/> names them in German.
/// </summary>
public static class XlsxReader
{
    public static Workbook Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var workbookPart = package.WorkbookPart ?? throw new InvalidDataException("Die Datei enthält keine Arbeitsmappe.");
        var workbook = new Workbook();
        var context = new ReadContext(workbook, workbookPart);

        var root = workbookPart.Workbook ?? throw new InvalidDataException("Die Datei enthält keine Arbeitsmappe.");
        workbook.Date1904 = root.WorkbookProperties?.Date1904?.Value ?? false;
        if (root.CalculationProperties is { } calc)
        {
            workbook.CalculationSettings = new CalculationSettings
            {
                Manual = calc.CalculationMode?.Value == X.CalculateModeValues.Manual,
                Iterate = calc.Iterate?.Value ?? false,
                MaxIterations = (int)(calc.IterateCount?.Value ?? 100),
                MaxChange = calc.IterateDelta?.Value ?? 0.001,
                FullCalculationOnLoad = calc.FullCalculationOnLoad?.Value ?? false,
            };
        }

        workbook.Theme = ReadTheme(workbookPart.ThemePart) ?? ThemeColors.Office;
        context.StyleMap = XlsxStyles.Read(workbookPart.WorkbookStylesPart?.Stylesheet, workbook.Styles);
        context.SharedStrings = ReadSharedStrings(workbookPart.SharedStringTablePart, context);
        context.DynamicArrayMetadata = ReadDynamicArrayMetadata(workbookPart);

        // Sheets first (names), so formulas can refer to any sheet.
        var entries = new List<(Worksheet Sheet, WorksheetPart Part)>();
        var position = 0;
        foreach (var entry in root.Sheets?.Elements<X.Sheet>() ?? [])
        {
            var part = entry.Id?.Value is { } id ? workbookPart.GetPartById(id) : null;
            if (part is not WorksheetPart worksheetPart)
            {
                context.Note(part is ChartsheetPart ? "Diagrammblätter" : "Makro- oder Dialogblätter");
                context.PreservedSheets.Add((position, entry.OuterXml));
                position++;
                continue;
            }

            var sheet = workbook.AddSheet(entry.Name?.Value ?? workbook.NextSheetName());
            sheet.Visibility = entry.State?.Value == X.SheetStateValues.Hidden ? SheetVisibility.Hidden
                : entry.State?.Value == X.SheetStateValues.VeryHidden ? SheetVisibility.VeryHidden : SheetVisibility.Visible;
            sheet.PartUri = worksheetPart.Uri.ToString();
            sheet.FileSheetId = entry.SheetId?.Value ?? 0;
            entries.Add((sheet, worksheetPart));
            position++;
        }

        if (workbook.Sheets.Count == 0)
        {
            workbook.AddSheet();
        }

        var activeTab = (int)(root.BookViews?.Elements<X.WorkbookView>().FirstOrDefault()?.ActiveTab?.Value ?? 0);
        workbook.ActiveSheetIndex = Math.Clamp(activeTab, 0, workbook.Sheets.Count - 1);
        ReadNames(root, workbook, context);
        foreach (var (sheet, part) in entries)
        {
            XlsxSheetReader.Read(sheet, part, context);
            XlsxDrawings.Read(sheet, part, context);
        }

        DetectUnsupported(package, workbookPart, context);
        workbook.OriginalPackage = bytes;
        workbook.HasMacros = workbookPart.VbaProjectPart is not null;
        workbook.PreservedSheets = context.PreservedSheets;
        workbook.UnsupportedContent = context.Notes;
        workbook.Calculation.Rebuild(markDirty: workbook.CalculationSettings.FullCalculationOnLoad);
        if (workbook.CalculationSettings.FullCalculationOnLoad)
        {
            workbook.Calculation.Recalculate();
        }

        workbook.ForgetChanges();
        return workbook;
    }

    private static void ReadNames(X.Workbook root, Workbook workbook, ReadContext context)
    {
        foreach (var name in root.DefinedNames?.Elements<X.DefinedName>() ?? [])
        {
            var text = name.Text ?? string.Empty;
            int? sheetId = name.LocalSheetId?.Value is { } local && local < workbook.Sheets.Count ? workbook.Sheets[(int)local].Id : null;
            try
            {
                var node = FormulaParser.ParseNode(text, workbook, default);
                text = FormulaFormatter.Format(node, workbook, default);
            }
            catch (FormulaParseException)
            {
                // Kept as written.
            }

            workbook.Names.Add(new DefinedName(name.Name?.Value ?? "Name", text, sheetId, name.Hidden?.Value ?? false, name.Comment?.Value));
        }

        _ = context;
    }

    private static ThemeColors? ReadTheme(ThemePart? part)
    {
        var scheme = part?.Theme?.ThemeElements?.ColorScheme;
        if (scheme is null)
        {
            return null;
        }

        static ThixOffice.Text.Drawing.RgbColor ColorOf(OpenXmlCompositeElement? element, ThixOffice.Text.Drawing.RgbColor fallback)
        {
            if (element?.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value is { } hex)
            {
                return ThixOffice.Text.Drawing.RgbColor.FromHex(hex);
            }

            if (element?.GetFirstChild<A.SystemColor>()?.LastColor?.Value is { } last)
            {
                return ThixOffice.Text.Drawing.RgbColor.FromHex(last);
            }

            return fallback;
        }

        var office = ThemeColors.Office;
        return new ThemeColors([
            ColorOf(scheme.Light1Color, office[0]), ColorOf(scheme.Dark1Color, office[1]), ColorOf(scheme.Light2Color, office[2]),
            ColorOf(scheme.Dark2Color, office[3]), ColorOf(scheme.Accent1Color, office[4]), ColorOf(scheme.Accent2Color, office[5]),
            ColorOf(scheme.Accent3Color, office[6]), ColorOf(scheme.Accent4Color, office[7]), ColorOf(scheme.Accent5Color, office[8]),
            ColorOf(scheme.Accent6Color, office[9]), ColorOf(scheme.Hyperlink, office[10]), ColorOf(scheme.FollowedHyperlinkColor, office[11]),
        ]);
    }

    private static string[] ReadSharedStrings(SharedStringTablePart? part, ReadContext context)
    {
        if (part is null)
        {
            return [];
        }

        var strings = new List<string>();
        using var reader = OpenXmlReader.Create(part);
        while (reader.Read())
        {
            if (reader.ElementType == typeof(X.SharedStringItem) && reader.IsStartElement)
            {
                var item = (X.SharedStringItem)reader.LoadCurrentElement()!;
                if (item.Elements<X.Run>().Any())
                {
                    context.Note("Formatierter Text in Zellen (wird als einfacher Text gespeichert)");
                }

                strings.Add(TextOf(item));
            }
        }

        return strings.ToArray();
    }

    /// <summary>The plain text of a shared string or inline string (runs concatenated, phonetic runs left out).</summary>
    internal static string TextOf(OpenXmlCompositeElement item)
    {
        if (item.GetFirstChild<X.Text>() is { } text && !item.Elements<X.Run>().Any())
        {
            return Unescape(text.Text);
        }

        return Unescape(string.Concat(item.Elements<X.Run>().Select(run => run.Text?.Text ?? string.Empty)));
    }

    /// <summary>_x000D_ style escapes of SpreadsheetML strings.</summary>
    internal static string Unescape(string text)
    {
        if (!text.Contains("_x", StringComparison.Ordinal))
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '_' && index + 6 < text.Length && text[index + 1] == 'x' && text[index + 6] == '_'
                && int.TryParse(text.AsSpan(index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
            {
                builder.Append((char)code);
                index += 6;
                continue;
            }

            builder.Append(text[index]);
        }

        return builder.ToString();
    }

    /// <summary>The cell metadata indices (cm, 1-based) that mark dynamic arrays (XLDAPR).</summary>
    private static HashSet<uint> ReadDynamicArrayMetadata(WorkbookPart workbookPart)
    {
        var result = new HashSet<uint>();
        var metadata = workbookPart.CellMetadataPart?.Metadata;
        if (metadata is null)
        {
            return result;
        }

        var types = metadata.MetadataTypes?.Elements<X.MetadataType>().ToList() ?? [];
        var dynamicType = types.FindIndex(type => type.Name?.Value == "XLDAPR") + 1;
        if (dynamicType == 0)
        {
            return result;
        }

        uint index = 0;
        foreach (var block in metadata.GetFirstChild<X.CellMetadata>()?.Elements<X.MetadataBlock>() ?? [])
        {
            index++;
            if (block.Elements<X.MetadataRecord>().Any(record => record.TypeIndex?.Value == (uint)dynamicType))
            {
                result.Add(index);
            }
        }

        return result;
    }

    private static void DetectUnsupported(SpreadsheetDocument package, WorkbookPart workbookPart, ReadContext context)
    {
        foreach (var worksheet in workbookPart.WorksheetParts)
        {
            if (worksheet.DrawingsPart is { } drawings)
            {
                if (drawings.ImageParts.Any())
                {
                    context.Note("Bilder");
                }

                if (!drawings.ChartParts.Any() && !drawings.ImageParts.Any())
                {
                    context.Note("Formen und Zeichnungen");
                }
            }

            if (worksheet.PivotTableParts.Any())
            {
                context.Note("PivotTables");
            }

            if (worksheet.WorksheetThreadedCommentsParts.Any())
            {
                context.Note("Kommentare mit Unterhaltungen (als Notizen angezeigt)");
            }

            if (worksheet.EmbeddedObjectParts.Any() || worksheet.EmbeddedPackageParts.Any())
            {
                context.Note("Eingebettete Objekte");
            }

            if (worksheet.ControlPropertiesParts.Any())
            {
                context.Note("Steuerelemente");
            }

            if (worksheet.SlicersParts.Any())
            {
                context.Note("Datenschnitte");
            }
        }

        if (workbookPart.VbaProjectPart is not null)
        {
            context.Note("Makros (werden beibehalten, aber nie ausgeführt)");
        }

        if (workbookPart.ExternalWorkbookParts.Any())
        {
            context.Note("Verknüpfungen mit anderen Arbeitsmappen");
        }

        if (workbookPart.ConnectionsPart is not null)
        {
            context.Note("Datenverbindungen und Abfragen");
        }

        if (workbookPart.PivotTableCacheDefinitionParts.Any() && !context.Notes.Contains("PivotTables"))
        {
            context.Note("PivotTables");
        }

        _ = package;
    }
}

/// <summary>What reading collects across parts.</summary>
internal sealed class ReadContext(Workbook workbook, WorkbookPart workbookPart)
{
    private readonly List<string> _notes = [];

    public Workbook Workbook { get; } = workbook;

    public WorkbookPart WorkbookPart { get; } = workbookPart;

    public int[] StyleMap { get; set; } = [0];

    public string[] SharedStrings { get; set; } = [];

    public HashSet<uint> DynamicArrayMetadata { get; set; } = [];

    public List<(int Position, string Xml)> PreservedSheets { get; } = [];

    public IReadOnlyList<string> Notes => _notes;

    public void Note(string text)
    {
        if (!_notes.Contains(text))
        {
            _notes.Add(text);
        }
    }

    public int Style(uint? index) => index is { } value && value < StyleMap.Length ? StyleMap[value] : 0;
}

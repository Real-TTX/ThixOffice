using System.Text;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Model;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace ThixCalc.Core.Formats.Xlsx;

/// <summary>
/// Writes a <see cref="Workbook"/> as .xlsx/.xlsm. A workbook read from a file is written INTO A COPY OF ITS ORIGINAL
/// PACKAGE: worksheet XML, styles, shared strings, the sheet list, names and the calculation settings are replaced; every
/// other part and relationship (drawings, charts, pivot tables, tables, VBA, external links, printer settings …) stays, and
/// worksheet elements the model does not own are written back where they were. Worksheets are streamed with an XmlWriter.
/// </summary>
public static class XlsxWriter
{
    private const string DynamicArrayExtension = "{bdbb8cdc-fa1e-496e-a857-3c3f30c029c3}";

    /// <summary>Writes the workbook; <paramref name="keepMacros"/> false drops a VBA project (saving an .xlsm as .xlsx).</summary>
    public static void Write(Workbook workbook, Stream stream, bool keepMacros = true)
    {
        using var buffer = new MemoryStream();
        if (workbook.OriginalPackage is { } original)
        {
            buffer.Write(original);
            buffer.Position = 0;
            using var package = SpreadsheetDocument.Open(buffer, true);
            if (!keepMacros && package.WorkbookPart?.VbaProjectPart is { } vba)
            {
                package.WorkbookPart.DeletePart(vba);
                package.ChangeDocumentType(SpreadsheetDocumentType.Workbook);
            }

            Fill(workbook, package);
        }
        else
        {
            using var package = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook);
            package.AddWorkbookPart().Workbook = new X.Workbook(new X.Sheets());
            package.PackageProperties.Created = DateTime.UtcNow;
            package.PackageProperties.Modified = DateTime.UtcNow;
            Fill(workbook, package);
        }

        buffer.Position = 0;
        buffer.CopyTo(stream);
    }

    private static void Fill(Workbook workbook, SpreadsheetDocument package)
    {
        var workbookPart = package.WorkbookPart!;
        var root = workbookPart.Workbook ??= new X.Workbook(new X.Sheets());
        package.PackageProperties.Modified = DateTime.UtcNow;

        var stylesPart = workbookPart.WorkbookStylesPart ?? workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = XlsxStyles.Write(workbook.Styles);
        stylesPart.Stylesheet.Save();

        var context = new WriteContext(workbook, workbookPart, DynamicArrayIndex(workbookPart));
        var parts = workbookPart.WorksheetParts.ToDictionary(part => part.Uri.ToString());
        var written = new List<(Worksheet Sheet, string RelationshipId)>();
        foreach (var sheet in workbook.Sheets)
        {
            var part = sheet.PartUri is { } uri && parts.Remove(uri, out var existing) ? existing : workbookPart.AddNewPart<WorksheetPart>();
            XlsxSheetWriter.Write(sheet, part, context);
            sheet.PartUri = part.Uri.ToString();
            written.Add((sheet, workbookPart.GetIdOfPart(part)));
        }

        foreach (var orphan in parts.Values)
        {
            workbookPart.DeletePart(orphan);
        }

        if (workbookPart.CalculationChainPart is { } chain)
        {
            // Excel rebuilds it; a stale chain makes it repair the file.
            workbookPart.DeletePart(chain);
        }

        WriteSharedStrings(workbookPart, context);
        if (context.UsesDynamicArrays && context.DynamicArrayIndex is null)
        {
            WriteDynamicArrayMetadata(workbookPart);
        }

        WriteWorkbookXml(workbook, root, written);
        root.Save();
    }

    private static void WriteWorkbookXml(Workbook workbook, X.Workbook root, List<(Worksheet Sheet, string RelationshipId)> written)
    {
        // The sheet list: worksheets in model order, chart sheets and the like back at their positions.
        var entries = new List<OpenXmlElement>();
        var usedIds = new HashSet<uint>(written.Select(entry => entry.Sheet.FileSheetId).Where(id => id != 0));
        var preserved = workbook.PreservedSheets.Select(entry => (entry.Position, Element: ParseSheet(entry.Xml))).ToList();
        foreach (var (_, element) in preserved)
        {
            usedIds.Add(((X.Sheet)element).SheetId?.Value ?? 0);
        }

        uint nextId = usedIds.Count == 0 ? 1 : usedIds.Max() + 1;
        foreach (var (sheet, relationship) in written)
        {
            if (sheet.FileSheetId == 0)
            {
                sheet.FileSheetId = nextId++;
            }

            var entry = new X.Sheet { Name = sheet.Name, SheetId = sheet.FileSheetId, Id = relationship };
            if (sheet.Visibility != SheetVisibility.Visible)
            {
                entry.State = sheet.Visibility == SheetVisibility.Hidden ? X.SheetStateValues.Hidden : X.SheetStateValues.VeryHidden;
            }

            entries.Add(entry);
        }

        foreach (var (position, element) in preserved.OrderBy(entry => entry.Position))
        {
            entries.Insert(Math.Min(position, entries.Count), element);
        }

        root.Sheets = new X.Sheets(entries);

        var sheetIndex = entries.Select((element, index) => (element, index))
            .Where(pair => pair.element is X.Sheet { Id: not null })
            .ToDictionary(pair => ((X.Sheet)pair.element).Id!.Value!, pair => pair.index);
        var names = new X.DefinedNames();
        foreach (var name in workbook.Names)
        {
            var element = new X.DefinedName { Name = name.Name, Text = XlsxFeatures.FileText(name.Formula, workbook, default) };
            if (name.SheetId is { } id && written.FirstOrDefault(entry => entry.Sheet.Id == id) is { Sheet: not null } local)
            {
                element.LocalSheetId = (uint)sheetIndex[local.RelationshipId];
            }

            if (name.Hidden)
            {
                element.Hidden = true;
            }

            if (name.Comment is { } comment)
            {
                element.Comment = comment;
            }

            names.Append(element);
        }

        root.DefinedNames = names.HasChildren ? names : null;

        root.WorkbookProperties ??= new X.WorkbookProperties();
        root.WorkbookProperties.Date1904 = workbook.Date1904 ? true : null;
        var settings = workbook.CalculationSettings;
        root.CalculationProperties ??= new X.CalculationProperties { CalculationId = 191029 };
        root.CalculationProperties.CalculationMode = settings.Manual ? X.CalculateModeValues.Manual : null;
        root.CalculationProperties.Iterate = settings.Iterate ? true : null;
        root.CalculationProperties.IterateCount = settings.Iterate ? (uint)settings.MaxIterations : null;
        root.CalculationProperties.IterateDelta = settings.Iterate ? settings.MaxChange : null;
        root.CalculationProperties.FullCalculationOnLoad = settings.FullCalculationOnLoad ? true : null;
        root.BookViews ??= new X.BookViews(new X.WorkbookView());
        var view = root.BookViews.GetFirstChild<X.WorkbookView>() ?? root.BookViews.AppendChild(new X.WorkbookView());
        var active = Math.Clamp(workbook.ActiveSheetIndex, 0, written.Count - 1);
        var activeEntry = written.Count > 0 ? sheetIndex[written[active].RelationshipId] : 0;
        view.ActiveTab = activeEntry > 0 ? (uint)activeEntry : null;
    }

    /// <summary>A preserved &lt;sheet&gt; entry (chart sheets …) back as an element.</summary>
    private static OpenXmlElement ParseSheet(string xml)
    {
        var holder = new X.Sheets($"<sheets xmlns=\"{XlsxNamespaces.Main}\" xmlns:r=\"{XlsxNamespaces.Relationships}\">{xml}</sheets>");
        return holder.FirstChild!.CloneNode(true);
    }

    private static uint? DynamicArrayIndex(WorkbookPart workbookPart)
    {
        var metadata = workbookPart.CellMetadataPart?.Metadata;
        if (metadata is null)
        {
            return null;
        }

        var types = metadata.MetadataTypes?.Elements<X.MetadataType>().ToList() ?? [];
        var dynamicType = types.FindIndex(type => type.Name?.Value == "XLDAPR") + 1;
        if (dynamicType == 0)
        {
            return 0; // a metadata part without dynamic arrays: write them as legacy array formulas
        }

        uint index = 0;
        foreach (var block in metadata.GetFirstChild<X.CellMetadata>()?.Elements<X.MetadataBlock>() ?? [])
        {
            index++;
            if (block.Elements<X.MetadataRecord>().Any(record => record.TypeIndex?.Value == (uint)dynamicType))
            {
                return index;
            }
        }

        return 0;
    }

    private static void WriteDynamicArrayMetadata(WorkbookPart workbookPart)
    {
        var part = workbookPart.CellMetadataPart ?? workbookPart.AddNewPart<CellMetadataPart>();
        const string xml =
            "<metadata xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:xda=\"http://schemas.microsoft.com/office/spreadsheetml/2017/dynamicarray\">"
            + "<metadataTypes count=\"1\"><metadataType name=\"XLDAPR\" minSupportedVersion=\"120000\" copy=\"1\" pasteAll=\"1\" pasteValues=\"1\" merge=\"1\" "
            + "splitFirst=\"1\" rowColShift=\"1\" clearFormats=\"1\" clearComments=\"1\" assign=\"1\" coerce=\"1\" cellMeta=\"1\"/></metadataTypes>"
            + "<futureMetadata name=\"XLDAPR\" count=\"1\"><bk><extLst><ext uri=\"" + DynamicArrayExtension + "\">"
            + "<xda:dynamicArrayProperties fDynamic=\"1\" fCollapsed=\"0\"/></ext></extLst></bk></futureMetadata>"
            + "<cellMetadata count=\"1\"><bk><rc t=\"1\" v=\"0\"/></bk></cellMetadata></metadata>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        part.FeedData(stream);
    }

    private static void WriteSharedStrings(WorkbookPart workbookPart, WriteContext context)
    {
        var part = workbookPart.SharedStringTablePart ?? workbookPart.AddNewPart<SharedStringTablePart>();
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
        {
            writer.WriteStartDocument(true);
            writer.WriteStartElement("sst", XlsxNamespaces.Main);
            writer.WriteAttributeString("count", context.StringUses.ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteAttributeString("uniqueCount", context.Strings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var text in context.Strings)
            {
                writer.WriteStartElement("si", XlsxNamespaces.Main);
                writer.WriteStartElement("t", XlsxNamespaces.Main);
                if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])))
                {
                    writer.WriteAttributeString("xml", "space", null, "preserve");
                }

                writer.WriteString(Escape(text));
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        stream.Position = 0;
        part.FeedData(stream);
    }

    /// <summary>Characters XML cannot hold become _xHHHH_ (and a literal "_x" is escaped itself).</summary>
    internal static string Escape(string text)
    {
        StringBuilder? builder = null;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            var invalid = (character < 0x20 && character is not '\t' and not '\n' and not '\r') || character is '￾' or '￿'
                || (char.IsSurrogate(character) && !(char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                    && !(char.IsLowSurrogate(character) && index > 0 && char.IsHighSurrogate(text[index - 1])));
            var literal = character == '_' && index + 6 < text.Length && text[index + 1] == 'x' && text[index + 6] == '_';
            if (!invalid && !literal)
            {
                builder?.Append(character);
                continue;
            }

            builder ??= new StringBuilder(text, 0, index, text.Length + 16);
            builder.Append(literal ? "_x005F_" : $"_x{(int)character:X4}_");
        }

        return builder?.ToString() ?? text;
    }
}

/// <summary>What writing collects across parts: the shared strings and the dynamic-array metadata index.</summary>
internal sealed class WriteContext(Workbook workbook, WorkbookPart workbookPart, uint? dynamicArrayIndex)
{
    private readonly Dictionary<string, int> _stringIndex = new(StringComparer.Ordinal);

    public Workbook Workbook { get; } = workbook;

    public WorkbookPart WorkbookPart { get; } = workbookPart;

    /// <summary>The cm index of dynamic arrays: null = none yet (one is created), 0 = not available (legacy arrays).</summary>
    public uint? DynamicArrayIndex { get; } = dynamicArrayIndex;

    public bool UsesDynamicArrays { get; set; }

    private HashSet<uint>? _tableIds;

    /// <summary>A table id unique in the workbook: <paramref name="preferred"/> when it is free, else the next one.</summary>
    public uint NextTableId(uint preferred)
    {
        _tableIds ??= WorkbookPart.WorksheetParts.SelectMany(part => part.TableDefinitionParts).Select(part => part.Table?.Id?.Value ?? 0).ToHashSet();
        if (preferred != 0 && _tableIds.Add(preferred))
        {
            return preferred;
        }

        var id = _tableIds.DefaultIfEmpty(0u).Max() + 1;
        _tableIds.Add(id);
        return id;
    }

    public List<string> Strings { get; } = [];

    public long StringUses { get; private set; }

    public int StringIndex(string text)
    {
        StringUses++;
        if (_stringIndex.TryGetValue(text, out var index))
        {
            return index;
        }

        index = Strings.Count;
        Strings.Add(text);
        _stringIndex[text] = index;
        return index;
    }
}

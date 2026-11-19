using ThixCalc.Core.Editing;
using ThixCalc.Core.Formats;
using ThixCalc.Core.Formats.Csv;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Documents;

/// <summary>
/// The document of a ThixCalc window: the workbook (<see cref="Workbook"/>) with its editing session (<see cref="Session"/>).
/// Reading sniffs the bytes – a zip package is ods (by its mimetype) or xlsx/xlsm, anything else CSV (separator and encoding detected); writing uses
/// <see cref="SaveFormat"/> (the format it was read in; the app sets it before "Speichern unter" into another type).
/// Every finished command, undo and redo marks the document unsaved.
/// </summary>
public sealed class CalcDocument : OfficeDocument
{
    public CalcDocument(string untitledName, CalcLocale? locale = null)
        : this(untitledName, Workbook.Create((locale ?? CalcLocale.German).SheetBaseName + "1"), locale)
    {
    }

    public CalcDocument(string untitledName, Workbook workbook, CalcLocale? locale = null)
        : base(untitledName)
    {
        Locale = locale ?? CalcLocale.German;
        Attach(workbook);
    }

    /// <summary>Raised when <see cref="Workbook"/> and <see cref="Session"/> were replaced (a file was read into this document).</summary>
    public event EventHandler? ContentReplaced;

    public Workbook Workbook { get; private set; } = null!;

    public EditingSession Session { get; private set; } = null!;

    public CalcLocale Locale { get; }

    public WorkbookFileFormat LoadedFormat { get; private set; } = WorkbookFileFormat.Xlsx;

    public WorkbookFileFormat SaveFormat { get; set; } = WorkbookFileFormat.Xlsx;

    /// <summary>The CSV options a CSV file was read with (written back the same way).</summary>
    public CsvOptions CsvOptions { get; set; } = new();

    /// <summary>The bytes of the CSV file this document was read from (Text-Import reads them again with other choices).</summary>
    public byte[]? CsvSource { get; private set; }

    /// <summary>What reading the CSV file guessed (separator, encoding) and whether the guess should be confirmed.</summary>
    public CsvDetection? CsvDetection { get; private set; }

    private string CsvSheetName => Path.GetFileNameWithoutExtension(FilePath) is { Length: > 0 and <= 31 } name ? name : Locale.SheetBaseName + "1";

    /// <summary>Text-Import: reads the CSV file again with another separator or encoding (no undo step, no unsaved change).</summary>
    public void ReloadCsv(CsvOptions options)
    {
        if (CsvSource is not { } source)
        {
            return;
        }

        CsvOptions = options with { Locale = Locale };
        Attach(CsvFormat.Read(new MemoryStream(source), CsvOptions, CsvSheetName));
        RaiseChanged();
    }

    public override bool CanUndo => Session.CanUndo;

    public override bool CanRedo => Session.CanRedo;

    public override void Undo() => Session.Undo();

    public override void Redo() => Session.Redo();

    /// <summary>Puts in a workbook without an undo step and without an unsaved change (samples, templates).</summary>
    public void Load(Workbook workbook)
    {
        Attach(workbook);
        RaiseChanged();
    }

    protected override void Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        var isZip = WorkbookFormats.IsZip(buffer.GetBuffer().AsSpan(0, (int)Math.Min(4, buffer.Length)));
        if (isZip && Formats.Ods.OdsReader.IsOds(buffer))
        {
            Attach(WorkbookFormats.For(WorkbookFileFormat.Ods).Read(buffer));
            LoadedFormat = WorkbookFileFormat.Ods;
        }
        else if (isZip)
        {
            var workbook = WorkbookFormats.For(WorkbookFileFormat.Xlsx).Read(buffer);
            LoadedFormat = workbook.HasMacros ? WorkbookFileFormat.Xlsm : WorkbookFileFormat.Xlsx;
            Attach(workbook);
        }
        else
        {
            CsvSource = buffer.ToArray();
            CsvDetection = CsvFormat.Detect(CsvSource, Locale);
            CsvOptions = new CsvOptions { Locale = Locale, Delimiter = CsvDetection.Delimiter, Encoding = CsvDetection.Encoding };
            Attach(CsvFormat.Read(new MemoryStream(CsvSource), CsvOptions, CsvSheetName));
            LoadedFormat = WorkbookFileFormat.Csv;
        }

        SaveFormat = LoadedFormat;
    }

    protected override void Write(Stream stream) => Write(stream, SaveFormat);

    protected override void Write(Stream stream, DocumentType type) => Write(stream, WorkbookFormats.FromExtension(type.Extension) ?? SaveFormat);

    protected override void OnSaved(DocumentType type) => SaveFormat = WorkbookFormats.FromExtension(type.Extension) ?? SaveFormat;

    private void Write(Stream stream, WorkbookFileFormat format) => WorkbookFormats.For(format, CsvOptions with { Locale = Locale }).Write(Workbook, stream);

    private void Attach(Workbook workbook)
    {
        if (Session is not null)
        {
            Session.CellsChanged -= OnCellsChanged;
        }

        Workbook = workbook;
        workbook.Calculation.Locale = Locale;
        Session = new EditingSession(workbook);
        Session.CellsChanged += OnCellsChanged;
        ContentReplaced?.Invoke(this, EventArgs.Empty);
    }

    private void OnCellsChanged(object? sender, WorkbookChangedEventArgs e) => OnContentChanged();
}

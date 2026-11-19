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
/// Reading sniffs the bytes – a zip package is xlsx/xlsm, anything else CSV (separator and encoding detected); writing uses
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
        if (WorkbookFormats.IsZip(buffer.GetBuffer().AsSpan(0, (int)Math.Min(4, buffer.Length))))
        {
            var workbook = WorkbookFormats.For(WorkbookFileFormat.Xlsx).Read(buffer);
            LoadedFormat = workbook.HasMacros ? WorkbookFileFormat.Xlsm : WorkbookFileFormat.Xlsx;
            Attach(workbook);
        }
        else
        {
            CsvOptions = new CsvOptions { Locale = Locale };
            Attach(CsvFormat.Read(buffer, CsvOptions, Locale.SheetBaseName + "1"));
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

using System.Text;
using ThixOffice.Core.Documents;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Formats.Text;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Documents;

/// <summary>The file formats a <see cref="WordDocument"/> reads and writes.</summary>
public enum WordFileFormat
{
    Docx,
    PlainText,
}

/// <summary>
/// The document of a ThixWrite window (phase 1a; replaces the phase-0 <see cref="TextDocument"/>): the model
/// (<see cref="Content"/>) with its editing session (<see cref="Session"/>: selection, commands, undo). Reading sniffs the
/// bytes – a zip package is docx, anything else plain text (BOM / UTF-8 / Windows-1252); writing uses
/// <see cref="SaveFormat"/> (the format it was read in; the app sets it before "Speichern unter" into another type).
/// Every finished command, undo and redo marks the document unsaved.
/// </summary>
public sealed class WordDocument : OfficeDocument
{
    public WordDocument(string untitledName)
        : base(untitledName)
    {
        Attach(Document.CreateBlank());
    }

    /// <summary>Raised when <see cref="Content"/> and <see cref="Session"/> were replaced (a file was read into this document).</summary>
    public event EventHandler? ContentReplaced;

    public Document Content { get; private set; } = null!;

    public EditingSession Session { get; private set; } = null!;

    /// <summary>The format the content came from.</summary>
    public WordFileFormat LoadedFormat { get; private set; } = WordFileFormat.Docx;

    /// <summary>The format the next write uses.</summary>
    public WordFileFormat SaveFormat { get; set; } = WordFileFormat.Docx;

    /// <summary>The encoding a plain-text file had (written back the same way, UTF-8 without BOM for new ones).</summary>
    public Encoding? TextEncoding { get; private set; }

    public override bool CanUndo => Session.CanUndo;

    public override bool CanRedo => Session.CanRedo;

    public override void Undo() => Session.Undo();

    public override void Redo() => Session.Redo();

    /// <summary>The format of a file extension (".docx" / ".txt"), or null for formats ThixWrite does not write.</summary>
    public static WordFileFormat? FormatOf(string extension) => extension.ToLowerInvariant() switch
    {
        ".docx" => WordFileFormat.Docx,
        ".txt" => WordFileFormat.PlainText,
        _ => null,
    };

    /// <summary>Puts in a content without an undo step and without an unsaved change (sample documents, templates).</summary>
    public void Load(Document content)
    {
        Attach(content);
        RaiseChanged();
    }

    protected override void Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        if (IsZip(buffer))
        {
            Attach(DocxReader.Read(buffer));
            LoadedFormat = WordFileFormat.Docx;
            TextEncoding = null;
        }
        else
        {
            Attach(PlainTextFormat.Read(buffer, out var encoding));
            LoadedFormat = WordFileFormat.PlainText;
            TextEncoding = encoding;
        }

        SaveFormat = LoadedFormat;
    }

    protected override void Write(Stream stream)
    {
        if (SaveFormat == WordFileFormat.PlainText)
        {
            PlainTextFormat.Write(Content, stream, TextEncoding);
            return;
        }

        DocxWriter.Write(Content, stream);
    }

    private static bool IsZip(MemoryStream stream)
    {
        var bytes = stream.GetBuffer();
        return stream.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4;
    }

    private void Attach(Document content)
    {
        if (Session is not null)
        {
            Session.ContentChanged -= OnSessionContentChanged;
        }

        Content = content;
        Session = new EditingSession(content);
        Session.ContentChanged += OnSessionContentChanged;
        ContentReplaced?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionContentChanged(object? sender, EventArgs e) => OnContentChanged();
}

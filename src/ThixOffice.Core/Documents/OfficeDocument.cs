using ThixOffice.Core.Models;

namespace ThixOffice.Core.Documents;

/// <summary>
/// The document of one window, UI-free: an app derives its model from it (ThixWrite: the text). The base knows where the
/// document lives, whether it has unsaved changes and what undo can do; reading and writing the bytes is the model's,
/// reading and writing the FILE is <see cref="Business.DocumentService"/>'s (atomic save, recent list, recovery).
/// </summary>
public abstract class OfficeDocument
{
    /// <summary>A new, empty document named <paramref name="untitledName"/> ("Dokument1") until it is saved.</summary>
    protected OfficeDocument(string untitledName) => UntitledName = untitledName;

    /// <summary>Raised when the content, the unsaved state, the file or the undo state changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Names the recovery file of this document (it outlives a crash, never the document).</summary>
    public Guid RecoveryId { get; } = Guid.NewGuid();

    /// <summary>The file, or null while the document was never saved.</summary>
    public string? FilePath { get; private set; }

    /// <summary>The type the file has (null while never saved).</summary>
    public DocumentType? FileType { get; private set; }

    /// <summary>"Dokument1" – the name while there is no file.</summary>
    public string UntitledName { get; }

    /// <summary>What the title bar shows: the file name with its extension, or the untitled name.</summary>
    public string Title => FilePath is null ? UntitledName : Path.GetFileName(FilePath);

    /// <summary>True while there are changes that are not in the file.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>True for a document brought back from a recovery file after a crash (it has no file of its own until saved).</summary>
    public bool IsRecovered { get; internal set; }

    /// <summary>True for a new document nobody touched: a file opened in its place replaces it (like Word's "Dokument1").</summary>
    public bool IsPristine => FilePath is null && !IsDirty && !IsRecovered;

    public abstract bool CanUndo { get; }

    public abstract bool CanRedo { get; }

    public abstract void Undo();

    public abstract void Redo();

    /// <summary>Loads the content (the file is opened by the business layer).</summary>
    protected abstract void Read(Stream stream);

    /// <summary>Writes the content (into a temporary file that replaces the document once it is complete).</summary>
    protected abstract void Write(Stream stream);

    /// <summary>The business layer reads a file into the model through this (the model only sees a stream).</summary>
    internal void ReadContent(Stream stream) => Read(stream);

    /// <summary>The business layer writes the model into a file through this.</summary>
    internal void WriteContent(Stream stream) => Write(stream);

    /// <summary>The model calls this after every change of its content.</summary>
    protected void OnContentChanged()
    {
        IsDirty = true;
        RaiseChanged();
    }

    /// <summary>Tells the views that something changed without a new unsaved state (undo stack, selection).</summary>
    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    internal void MarkSaved(string path, DocumentType type)
    {
        FilePath = path;
        FileType = type;
        IsDirty = false;
        IsRecovered = false;
        RaiseChanged();
    }

    internal void MarkUnsaved()
    {
        IsDirty = true;
        RaiseChanged();
    }
}

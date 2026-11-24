using ThixOffice.Core.Formats.OfficeCrypto;
using ThixOffice.Core.Infrastructure;
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

    /// <summary>What the title bar shows: the file name with its extension, or the untitled name (a workspace may name what it shows: ThixMail's view).</summary>
    public virtual string Title => FilePath is null ? UntitledName : Path.GetFileName(FilePath);

    /// <summary>True while there are changes that are not in the file.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>True for a document brought back from a recovery file after a crash (it has no file of its own until saved).</summary>
    public bool IsRecovered { get; internal set; }

    /// <summary>True for a new document nobody touched: a file opened in its place replaces it (like Word's "Dokument1").</summary>
    public bool IsPristine => FilePath is null && !IsDirty && !IsRecovered;

    /// <summary>
    /// The ThixDrive item this document belongs to, or null for a local file. A Drive document's <see cref="FilePath"/> is its local
    /// working copy (<see cref="Business.DriveService"/>); saving writes it, then uploads a new version.
    /// </summary>
    public DriveOrigin? Drive { get; private set; }

    /// <summary>Where the upload of a ThixDrive document stands (<see cref="DriveSyncState.Uploaded"/> for a local file).</summary>
    public DriveSyncState DriveState { get; private set; }

    /// <summary>True for a document of ThixDrive.</summary>
    public bool IsInDrive => Drive is not null;

    /// <summary>Where the document lives, for people: the file path, "ThixDrive: /Projekte/Bericht.docx" or "Noch nicht gespeichert".</summary>
    public string LocationText => Drive is { } drive ? $"ThixDrive: {drive.DisplayPath}" : FilePath ?? "Noch nicht gespeichert";

    /// <summary>
    /// True for a document protected with "Mit Kennwort verschlüsseln": its saves (and recovery files) are encrypted. The password
    /// lives in memory only – never in settings, logs or the recent list.
    /// </summary>
    public bool HasPassword => Password is not null;

    /// <summary>The password of the document (null: none); only the business layer reads it.</summary>
    internal string? Password { get; private set; }

    /// <summary>
    /// Protects the document with <paramref name="password"/> (null or empty removes the protection). A change of the protection is a
    /// change that needs a save, like Word's; longer than 255 characters is refused at the field "Password".
    /// </summary>
    public void SetPassword(string? password)
    {
        password = string.IsNullOrEmpty(password) ? null : password;
        if (password is { Length: > OfficeEncryption.MaxPasswordLength })
        {
            throw new BusinessException(OfficeEncryption.TooLongText, OfficeEncryption.PasswordField);
        }

        if (password == Password)
        {
            return;
        }

        Password = password;
        MarkUnsaved();
    }

    public abstract bool CanUndo { get; }

    public abstract bool CanRedo { get; }

    public abstract void Undo();

    public abstract void Redo();

    /// <summary>Loads the content (the file is opened by the business layer).</summary>
    protected abstract void Read(Stream stream);

    /// <summary>Writes the content (into a temporary file that replaces the document once it is complete).</summary>
    protected abstract void Write(Stream stream);

    /// <summary>
    /// Writes the content as a file of <paramref name="type"/> ("Speichern unter" into another type: ThixWrite writes plain text
    /// for .txt). The default writes the model's own format.
    /// </summary>
    protected virtual void Write(Stream stream, DocumentType type) => Write(stream);

    /// <summary>The document was saved as a file of <paramref name="type"/> (the next save writes that type).</summary>
    protected virtual void OnSaved(DocumentType type)
    {
    }

    /// <summary>
    /// The business layer reads a file into the model through this (the model only sees a stream; <paramref name="stream"/> is the whole
    /// file and seekable). A file protected with "Mit Kennwort verschlüsseln" is decrypted first: without <paramref name="password"/> it is
    /// a <see cref="PasswordRequiredException"/> naming <paramref name="fileName"/>, a wrong one a <see cref="WrongPasswordException"/>; the
    /// password is kept for the next save. Other protected or foreign kinds (older encryption, IRM, a binary .doc, an encrypted
    /// OpenDocument file) are refused in words – never read as something else.
    /// </summary>
    internal void ReadContent(Stream stream, string fileName, string? password = null)
    {
        var kind = OfficeEncryption.Detect(stream);
        if (OfficeEncryption.Refusal(kind) is { } refusal)
        {
            throw refusal;
        }

        if (kind == EncryptedFileKind.None)
        {
            Read(stream);
            Password = null;
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new PasswordRequiredException(fileName);
        }

        using (var package = new MemoryStream(OfficeEncryption.Decrypt(stream, password), writable: false))
        {
            Read(package);
        }

        Password = password;
    }

    /// <summary>The business layer writes the model in its own format (the recovery file) through this.</summary>
    internal void WriteContent(Stream stream) => WriteProtected(stream, FileType ?? AppInfo.Definition.SaveType, Write);

    /// <summary>
    /// The business layer writes the model into a file of <paramref name="type"/> through this: encrypted with the document's password
    /// when it has one and the type can carry it (<see cref="DocumentType.CanEncrypt"/>), else as the model writes it.
    /// </summary>
    internal void WriteContentAs(Stream stream, DocumentType type) => WriteProtected(stream, type, target => Write(target, type));

    private void WriteProtected(Stream stream, DocumentType type, Action<Stream> write)
    {
        if (Password is not { } password || !type.CanEncrypt)
        {
            write(stream);
            return;
        }

        using var package = new MemoryStream();
        write(package);
        OfficeEncryption.Encrypt(package.GetBuffer().AsSpan(0, (int)package.Length), password, stream);
    }

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
        if (!type.CanEncrypt)
        {
            Password = null; // saved where a password cannot go (the window asked first): unprotected from now on
        }

        OnSaved(type);
        RaiseChanged();
    }

    /// <summary>The business layer ties the document to a ThixDrive item (null: a local file again) and sets its upload state.</summary>
    internal void SetDrive(DriveOrigin? origin, DriveSyncState state)
    {
        Drive = origin;
        DriveState = origin is null ? DriveSyncState.Uploaded : state;
        RaiseChanged();
    }

    internal void MarkUnsaved()
    {
        IsDirty = true;
        RaiseChanged();
    }
}

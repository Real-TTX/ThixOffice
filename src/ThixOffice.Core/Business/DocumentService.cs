using ThixOffice.Core.Documents;
using ThixOffice.Core.Formats.OfficeCrypto;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// Opening, saving and recovering the documents of an app – the rules of every document window: only supported types,
/// an atomic save, "Zuletzt verwendet" after every open and save, a recovery file while there are unsaved changes. The
/// model reads and writes its bytes (<see cref="OfficeDocument"/>), this class decides where they come from and go to.
/// </summary>
public static class DocumentService
{
    private static int _untitledCount;

    /// <summary>"Dokument1", "Dokument2" … – the names of new documents in this process.</summary>
    public static string NextUntitledName() => $"{AppInfo.Definition.UntitledBaseName}{Interlocked.Increment(ref _untitledCount)}";

    /// <summary>
    /// Opens <paramref name="path"/> into a document made by <paramref name="create"/> (it gets the untitled name). A file protected with
    /// a password needs <paramref name="password"/>: without it a <see cref="PasswordRequiredException"/>, a wrong one a
    /// <see cref="WrongPasswordException"/> (the UI asks and opens again).
    /// </summary>
    public static T Open<T>(string path, Func<string, T> create, string? password = null)
        where T : OfficeDocument
    {
        var fullPath = FullPath(path);
        var type = AppInfo.Definition.SupportedTypeOf(fullPath)
                   ?? throw new BusinessException($"{AppInfo.ProductName} kann „{Path.GetFileName(fullPath)}“ (noch) nicht öffnen. Unterstützt: {SupportedList()}.");
        var document = Load(fullPath, type, create, password);
        RecentDocuments.Add(fullPath);
        return document;
    }

    /// <summary>Reads a file into a new document of <paramref name="type"/> (also a ThixDrive working copy); not added to the recent list.</summary>
    internal static T Load<T>(string fullPath, DocumentType type, Func<string, T> create, string? password = null)
        where T : OfficeDocument
    {
        var document = create(NextUntitledName());
        using (var content = DocumentFiles.ReadAll(fullPath))
        {
            try
            {
                document.ReadContent(content, Path.GetFileName(fullPath), password);
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException or System.Text.DecoderFallbackException)
            {
                throw new BusinessException($"„{Path.GetFileName(fullPath)}“ ist beschädigt oder kein gültiges Dokument ({exception.Message}).");
            }
        }

        document.MarkSaved(fullPath, type);
        return document;
    }

    /// <summary>Saves the document into its own file ("Speichern"); a document without a file needs <see cref="SaveAs"/>.</summary>
    public static void Save(OfficeDocument document)
    {
        if (document.IsInDrive)
        {
            throw new BusinessException("Dieses Dokument liegt in ThixDrive und wird dort gespeichert.");
        }

        if (document.FilePath is null)
        {
            throw new BusinessException("Das Dokument hat noch keinen Speicherort. Bitte „Speichern unter“ verwenden.");
        }

        SaveAs(document, document.FilePath);
    }

    /// <summary>
    /// Saves the document as <paramref name="path"/> ("Speichern unter"). Without an extension the app's save type is added;
    /// a type the app cannot write is refused.
    /// </summary>
    public static string SaveAs(OfficeDocument document, string path)
    {
        var target = TargetPath(path);
        var type = AppInfo.Definition.SupportedTypeOf(target)!;
        DocumentFiles.SaveAtomic(target, stream => document.WriteContentAs(stream, type), SettingsService.Get().KeepBackupCopy);
        document.MarkSaved(target, type);
        if (document.IsInDrive)
        {
            document.SetDrive(null, DriveSyncState.Uploaded); // saved on this computer: a local file from now on
        }

        DocumentFiles.DeleteRecovery(document.RecoveryId);
        RecentDocuments.Add(target);
        Log.Info($"Document saved ({type.Name})");
        return target;
    }

    /// <summary>
    /// Exports a document into another format ("Als PDF exportieren"): <paramref name="write"/> fills the file, which is written
    /// atomically like a save; the document keeps its own file and its unsaved state.
    /// </summary>
    public static string Export(string path, string extension, Action<Stream> write)
    {
        var target = FullPath(path);
        if (!string.Equals(Path.GetExtension(target), extension, StringComparison.OrdinalIgnoreCase))
        {
            target += extension;
        }

        DocumentFiles.SaveAtomic(target, write);
        Log.Info($"Document exported ({extension})");
        return target;
    }

    /// <summary>
    /// The export type (PDF) a "Speichern unter" to <paramref name="path"/> means, or null for the document's own types. Such a save
    /// writes a copy through <see cref="Export"/>: the document keeps its file, its type and its unsaved changes (like Word).
    /// </summary>
    public static DocumentType? ExportTypeOf(string path) => string.IsNullOrWhiteSpace(path) ? null : AppInfo.Definition.ExportTypeOf(path.Trim());

    /// <summary>
    /// True when saving <paramref name="document"/> as <paramref name="path"/> drops its password: it has one and the type cannot carry
    /// it (.odt, .txt, .csv …). The window asks first (<see cref="PasswordLossQuestion"/>); the save then leaves the document unprotected.
    /// </summary>
    public static bool LosesPassword(OfficeDocument document, string path) =>
        document.HasPassword && !string.IsNullOrWhiteSpace(path) && ExportTypeOf(path) is null
        && AppInfo.Definition.SupportedTypeOf(path.Trim()) is { CanEncrypt: false };

    /// <summary>"„Bericht.odt“ kann nicht mit einem Kennwort geschützt werden. Ohne Kennwort speichern?"</summary>
    public static string PasswordLossQuestion(string path) =>
        $"„{Path.GetFileName(path.Trim())}“ kann nicht mit einem Kennwort geschützt werden. Ohne Kennwort speichern?";

    /// <summary>
    /// "Mit Kennwort verschlüsseln": the password typed twice protects the document from its next save on; both empty remove the
    /// protection of a document that has one. Refusals name their field (<see cref="OfficeEncryption.PasswordField"/>,
    /// <see cref="OfficeEncryption.ConfirmationField"/>).
    /// </summary>
    public static void SetPassword(OfficeDocument document, string? password, string? confirmation)
    {
        document.SetPassword(OfficeEncryption.CheckNewPassword(password, confirmation, document.HasPassword));
        Log.Info(document.HasPassword ? "A document was protected with a password" : "The password of a document was removed");
    }

    /// <summary>The full path a "Speichern unter" writes to: with the save type's extension when the name has none; checked.</summary>
    public static string TargetPath(string path)
    {
        var fullPath = FullPath(path);
        if (Path.GetExtension(fullPath).Length == 0)
        {
            fullPath += AppInfo.Definition.SaveType.Extension;
        }

        if (AppInfo.Definition.SupportedTypeOf(fullPath) is null)
        {
            throw new BusinessException($"Als „{Path.GetExtension(fullPath)}“ kann {AppInfo.ProductName} (noch) nicht speichern. Möglich: {SaveAsList()}.", "FileName");
        }

        return fullPath;
    }

    /// <summary>The name "Speichern unter" proposes: the file name, or the untitled name with the save type ("Dokument1.txt").</summary>
    public static string SuggestedFileName(OfficeDocument document) =>
        document.FilePath is { } path ? Path.GetFileName(path) : document.UntitledName + AppInfo.Definition.SaveType.Extension;

    /// <summary>The folder "Speichern unter" starts in: the document's own, else the default save folder.</summary>
    public static string SuggestedFolder(OfficeDocument document) =>
        !document.IsInDrive && document.FilePath is { } path &&Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder) ? folder : SettingsService.SaveFolder;

    // ===== Recovery =====

    /// <summary>Writes the recovery file of a document with unsaved changes (the window's timer calls it).</summary>
    public static void WriteRecovery(OfficeDocument document)
    {
        if (!document.IsDirty)
        {
            return;
        }

        var extension = document.FileType?.Extension ?? AppInfo.Definition.SaveType.Extension;
        DocumentFiles.WriteRecovery(new RecoveryInfo(document.RecoveryId, document.Title, document.FilePath, extension, DateTimeOffset.Now), document.WriteContent);
    }

    /// <summary>The document is saved or closed: its recovery file goes.</summary>
    public static void DiscardRecovery(OfficeDocument document) => DocumentFiles.DeleteRecovery(document.RecoveryId);

    /// <summary>What a crash left behind (the app offers it at its next start).</summary>
    public static IReadOnlyList<RecoveryInfo> RecoveredDocuments() => DocumentFiles.ListRecovery();

    /// <summary>
    /// Brings a document back from its recovery file: an unsaved document named "&lt;title&gt; (wiederhergestellt)"; the recovery
    /// file goes (the window writes a new one while it has unsaved changes). The recovery file of a protected document is encrypted
    /// like its file: it needs <paramref name="password"/> (<see cref="PasswordRequiredException"/>), and the document keeps it.
    /// </summary>
    public static T Recover<T>(RecoveryInfo info, Func<string, T> create, string? password = null)
        where T : OfficeDocument
    {
        var document = create($"{Path.GetFileNameWithoutExtension(info.Title)} (wiederhergestellt)");
        using (var content = DocumentFiles.ReadRecovery(info.Id))
        {
            try
            {
                document.ReadContent(content, info.Title, password);
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException or System.Text.DecoderFallbackException)
            {
                throw new BusinessException($"Die Wiederherstellungsdatei von „{info.Title}“ ist beschädigt ({exception.Message}).");
            }
        }

        document.IsRecovered = true;
        document.MarkUnsaved();
        DocumentFiles.DeleteRecovery(info.Id);
        return document;
    }

    /// <summary>Discards a recovery file the user did not want.</summary>
    public static void DiscardRecovery(RecoveryInfo info) => DocumentFiles.DeleteRecovery(info.Id);

    /// <summary>Tests start counting at Dokument1 again.</summary>
    internal static void ResetUntitledCountForTests() => _untitledCount = 0;

    /// <summary>".docx, .odt, .txt, .pdf" – what "Speichern unter" can write.</summary>
    internal static string SaveAsList() => string.Join(", ", AppInfo.Definition.SaveAsTypes.Select(type => type.Extension));

    private static string SupportedList() => string.Join(", ", AppInfo.Definition.SupportedTypes.Select(type => type.Extension));

    private static string FullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new BusinessException("Bitte einen Dateinamen angeben.", "FileName");
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new BusinessException("Dieser Dateiname ist ungültig.", "FileName");
        }
    }
}

using ThixOffice.Core.Documents;
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

    /// <summary>Opens <paramref name="path"/> into a document made by <paramref name="create"/> (it gets the untitled name).</summary>
    public static T Open<T>(string path, Func<string, T> create)
        where T : OfficeDocument
    {
        var fullPath = FullPath(path);
        var type = AppInfo.Definition.SupportedTypeOf(fullPath)
                   ?? throw new BusinessException($"{AppInfo.ProductName} kann „{Path.GetFileName(fullPath)}“ (noch) nicht öffnen. Unterstützt: {SupportedList()}.");
        var document = create(NextUntitledName());
        using (var content = DocumentFiles.ReadAll(fullPath))
        {
            try
            {
                document.ReadContent(content);
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException or System.Text.DecoderFallbackException)
            {
                throw new BusinessException($"„{Path.GetFileName(fullPath)}“ ist beschädigt oder kein gültiges Dokument ({exception.Message}).");
            }
        }

        document.MarkSaved(fullPath, type);
        RecentDocuments.Add(fullPath);
        return document;
    }

    /// <summary>Saves the document into its own file ("Speichern"); a document without a file needs <see cref="SaveAs"/>.</summary>
    public static void Save(OfficeDocument document)
    {
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
            throw new BusinessException($"Als „{Path.GetExtension(fullPath)}“ kann {AppInfo.ProductName} (noch) nicht speichern. Möglich: {SupportedList()}.", "FileName");
        }

        return fullPath;
    }

    /// <summary>The name "Speichern unter" proposes: the file name, or the untitled name with the save type ("Dokument1.txt").</summary>
    public static string SuggestedFileName(OfficeDocument document) =>
        document.FilePath is { } path ? Path.GetFileName(path) : document.UntitledName + AppInfo.Definition.SaveType.Extension;

    /// <summary>The folder "Speichern unter" starts in: the document's own, else the default save folder.</summary>
    public static string SuggestedFolder(OfficeDocument document) =>
        document.FilePath is { } path && Path.GetDirectoryName(path) is { } folder && Directory.Exists(folder) ? folder : SettingsService.SaveFolder;

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
    /// file goes (the window writes a new one while it has unsaved changes).
    /// </summary>
    public static T Recover<T>(RecoveryInfo info, Func<string, T> create)
        where T : OfficeDocument
    {
        var document = create($"{Path.GetFileNameWithoutExtension(info.Title)} (wiederhergestellt)");
        using (var content = DocumentFiles.ReadRecovery(info.Id))
        {
            document.ReadContent(content);
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

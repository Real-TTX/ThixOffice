using System.Text.Json;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>What a recovery file says about its document (the content lies next to it).</summary>
public sealed record RecoveryInfo(Guid Id, string Title, string? OriginalPath, string Extension, DateTimeOffset SavedAt);

/// <summary>
/// The bytes of documents on disk, safely:
///  - <see cref="SaveAtomic"/> writes into a temporary file in the same folder and only then replaces the document, so a
///    crash, a full disk or a lost network drive never leaves half a document; the previous version can stay as ".bak";
///  - <see cref="IsLockedByAnotherProgram"/> sees a document another program holds open for writing (Word, a sync tool);
///  - the recovery files keep unsaved changes of open documents (every few minutes) until the document is saved or closed;
///    after a crash the app offers them at its next start.
/// The UI never opens files itself: it calls <see cref="DocumentService"/>, which uses this class.
/// </summary>
public static class DocumentFiles
{
    private const string TemporarySuffix = ".~tmp";
    private const string BackupSuffix = ".bak";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Writes the document through <paramref name="write"/> into a temporary file and replaces <paramref name="path"/> with it.</summary>
    public static void SaveAtomic(string path, Action<Stream> write, bool keepBackup = false)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new BusinessException("Dieser Speicherort ist ungültig.");
        if (!Directory.Exists(folder))
        {
            throw new BusinessException("Den Ordner gibt es nicht (mehr). Bitte einen anderen Speicherort wählen.");
        }

        if (File.Exists(path) && IsLockedByAnotherProgram(path))
        {
            throw new BusinessException($"„{Path.GetFileName(path)}“ ist in einem anderen Programm geöffnet. Bitte dort schließen oder unter einem anderen Namen speichern.");
        }

        var temporary = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + TemporarySuffix);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                var backup = keepBackup ? path + BackupSuffix : null;
                if (backup is not null && File.Exists(backup))
                {
                    File.Delete(backup);
                }

                if (OperatingSystem.IsWindows())
                {
                    // Keeps the creation date, the attributes and the security of the document.
                    File.Replace(temporary, path, backup, ignoreMetadataErrors: true);
                    return;
                }

                if (backup is not null)
                {
                    File.Copy(path, backup, overwrite: true);
                }
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (UnauthorizedAccessException)
        {
            throw new BusinessException("Hier darf nicht gespeichert werden (keine Berechtigung oder schreibgeschützt). Bitte einen anderen Speicherort wählen.");
        }
        catch (IOException exception)
        {
            Log.Warning($"Saving failed: {exception.GetType().Name} {exception.HResult:X8}");
            throw new BusinessException($"Das Dokument konnte nicht gespeichert werden: {exception.Message}");
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <summary>Reads the whole document into memory (shared, so a document another program has open can still be read).</summary>
    public static MemoryStream ReadAll(string path)
    {
        if (!File.Exists(path))
        {
            throw new BusinessException($"„{Path.GetFileName(path)}“ wurde nicht gefunden. Wurde die Datei verschoben oder gelöscht?");
        }

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var memory = new MemoryStream(file.Length > 0 && file.Length < int.MaxValue ? (int)file.Length : 0);
            file.CopyTo(memory);
            memory.Position = 0;
            return memory;
        }
        catch (UnauthorizedAccessException)
        {
            throw new BusinessException($"„{Path.GetFileName(path)}“ darf nicht gelesen werden (keine Berechtigung).");
        }
        catch (IOException exception)
        {
            throw new BusinessException($"„{Path.GetFileName(path)}“ konnte nicht gelesen werden: {exception.Message}");
        }
    }

    /// <summary>True when another program holds the file open so that it cannot be written now.</summary>
    public static bool IsLockedByAnotherProgram(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false; // read-only: SaveAtomic says so in its own words
        }
        catch (IOException)
        {
            return true;
        }
    }

    // ===== Recovery files =====

    /// <summary>Writes the recovery file of a document with unsaved changes (replaces the previous one).</summary>
    public static void WriteRecovery(RecoveryInfo info, Action<Stream> write)
    {
        Directory.CreateDirectory(AppPaths.RecoveryDirectory);
        var data = DataFile(info.Id);
        var temporary = data + TemporarySuffix;
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            write(stream);
        }

        File.Move(temporary, data, overwrite: true);
        File.WriteAllText(InfoFile(info.Id), JsonSerializer.Serialize(info, JsonOptions));
    }

    /// <summary>The recovery files that are there (left by a crash when the app starts), newest first; broken ones are skipped.</summary>
    public static IReadOnlyList<RecoveryInfo> ListRecovery()
    {
        if (!Directory.Exists(AppPaths.RecoveryDirectory))
        {
            return [];
        }

        var found = new List<RecoveryInfo>();
        foreach (var file in Directory.EnumerateFiles(AppPaths.RecoveryDirectory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<RecoveryInfo>(File.ReadAllText(file), JsonOptions) is { } info && File.Exists(DataFile(info.Id)))
                {
                    found.Add(info);
                }
            }
            catch (Exception exception) when (exception is JsonException or IOException)
            {
                Log.Warning($"A recovery file could not be read: {exception.Message}");
            }
        }

        return [.. found.OrderByDescending(info => info.SavedAt)];
    }

    /// <summary>The content of a recovery file.</summary>
    public static MemoryStream ReadRecovery(Guid id) => ReadAll(DataFile(id));

    /// <summary>Removes the recovery file of a document (saved, closed without saving, or taken over).</summary>
    public static void DeleteRecovery(Guid id)
    {
        TryDelete(DataFile(id));
        TryDelete(InfoFile(id));
    }

    private static string DataFile(Guid id) => Path.Combine(AppPaths.RecoveryDirectory, id.ToString("N") + ".data");

    private static string InfoFile(Guid id) => Path.Combine(AppPaths.RecoveryDirectory, id.ToString("N") + ".json");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"A temporary file could not be removed: {exception.Message}");
        }
    }
}

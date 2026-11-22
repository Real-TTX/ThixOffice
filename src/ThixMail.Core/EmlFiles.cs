using ThixMail.Core.Mime;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;

namespace ThixMail.Core;

/// <summary>
/// Saved messages (.eml) and attachments as files, in the business layer: read an .eml (refused when it is not a message or too large), save a message
/// or an attachment atomically (a temporary file, then replaced), and the folder for opened attachments (a fresh temporary folder per file, so two
/// attachments with the same name never overwrite each other).
/// </summary>
public static class EmlFiles
{
    /// <summary>The largest .eml ThixMail opens (100 MB).</summary>
    public const long MaximumSize = 100L * 1024 * 1024;

    public static byte[] Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new BusinessException($"Die Datei „{Path.GetFileName(path)}“ gibt es nicht (mehr).");
        }

        var info = new FileInfo(path);
        if (info.Length > MaximumSize)
        {
            throw new BusinessException("Die Datei ist zu groß für eine E-Mail-Nachricht.");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BusinessException($"Die Datei lässt sich nicht lesen: {exception.Message}");
        }

        var message = MailMime.Parse(bytes);
        if (message.Headers.Count == 0 || (message.From.Count == 0 && message.To.Count == 0 && string.IsNullOrEmpty(message.Subject) && message.Date == DateTimeOffset.MinValue))
        {
            throw new BusinessException("Diese Datei ist keine E-Mail-Nachricht.");
        }

        return bytes;
    }

    /// <summary>Writes bytes atomically (also for an attachment saved where the person chose).</summary>
    public static void Save(string path, byte[] content)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(folder);
            var temporary = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BusinessException($"Die Datei lässt sich nicht speichern: {exception.Message}");
        }
    }

    /// <summary>Saves every attachment into a folder (names made unique: "Bericht (2).pdf"); answers the paths.</summary>
    public static IReadOnlyList<string> SaveAll(string folder, IReadOnlyList<MailAttachment> attachments)
    {
        var paths = new List<string>();
        foreach (var attachment in attachments)
        {
            var path = UniquePath(folder, MailReader.Sanitize(attachment.FileName));
            Save(path, attachment.Content);
            paths.Add(path);
        }

        return paths;
    }

    /// <summary>An attachment written to a temporary folder of its own for opening with its program (the UI warns before an executable type).</summary>
    public static string ForOpening(MailAttachment attachment)
    {
        var folder = Path.Combine(Path.GetTempPath(), "ThixMail", Guid.NewGuid().ToString("N")[..12]);
        var path = Path.Combine(folder, MailReader.Sanitize(attachment.FileName));
        Save(path, attachment.Content);
        return path;
    }

    /// <summary>A file name for "Speichern unter" of a message: its subject, made safe, ".eml".</summary>
    public static string SuggestedName(string subject) => MailReader.Sanitize(subject.Length == 0 ? "Nachricht" : subject) + ".eml";

    private static string UniquePath(string folder, string name)
    {
        var path = Path.Combine(folder, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var index = 2; File.Exists(path); index++)
        {
            path = Path.Combine(folder, $"{stem} ({index}){extension}");
        }

        return path;
    }

    internal static string DocumentsFolder => SettingsService.SaveFolder;
}

using System.Text;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Pim.Rules;

namespace ThixContacts.Core;

/// <summary>The formats ThixContacts writes.</summary>
public enum ContactExportFormat
{
    VCard3,
    VCard4,
    Csv,
}

/// <summary>A file chosen for an import: vCard (its contacts) or CSV (its table, whose columns the person matches first).</summary>
public sealed record ContactImportFile(string Path, IReadOnlyList<ImportedPerson>? Contacts, CsvTable? Table)
{
    public bool IsCsv => Table is not null;
}

/// <summary>
/// The files of ThixContacts (the business layer: the UI only picks paths): reading a file to import – vCard (2.1, 3.0, 4.0) or CSV, by its name
/// or its first line – and writing an export atomically (a temporary file, then the target).
/// </summary>
public static class ContactFiles
{
    /// <summary>The largest file to import (bytes).</summary>
    public const long MaxImportBytes = 64L * 1024 * 1024;

    /// <summary>Reads a file to import; refusals (too large, unreadable, nothing in it) as <see cref="BusinessException"/>.</summary>
    public static ContactImportFile Read(string path)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new BusinessException($"Die Datei „{System.IO.Path.GetFileName(path)}“ gibt es nicht.");
            }

            if (info.Length > MaxImportBytes)
            {
                throw new BusinessException($"Die Datei ist zu groß (höchstens {MaxImportBytes / 1024 / 1024} MB).");
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BusinessException($"Die Datei ließ sich nicht lesen: {exception.Message}");
        }

        if (IsVCard(path, bytes))
        {
            var contacts = VCard.Read(bytes);
            return contacts.Count == 0
                ? throw new BusinessException("In dieser Datei steht keine vCard mit einem Namen, einer Firma oder einer E-Mail-Adresse.")
                : new ContactImportFile(path, contacts, null);
        }

        var table = PersonCsv.Read(bytes);
        return table.Columns.Count == 0 || table.Rows.Count == 0
            ? throw new BusinessException("Diese Datei ist weder eine vCard noch eine CSV-Datei mit einer Kopfzeile und Zeilen darunter.")
            : new ContactImportFile(path, null, table);
    }

    /// <summary>vCard by its extension or its first line.</summary>
    public static bool IsVCard(string path, byte[] bytes)
    {
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".vcf" or ".vcard")
        {
            return true;
        }

        var head = VCard.TextOf(bytes.Length > 4096 ? bytes[..4096] : bytes).TrimStart('﻿', ' ', '\r', '\n', '\t');
        return head.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The extension of a format (".vcf", ".csv").</summary>
    public static string ExtensionOf(ContactExportFormat format) => format == ContactExportFormat.Csv ? ".csv" : ".vcf";

    /// <summary>Writes persons into <paramref name="path"/> (the extension added when missing), atomically; answers the path written.</summary>
    public static string Write(string path, IReadOnlyList<ExportedPerson> people, ContactExportFormat format)
    {
        if (people.Count == 0)
        {
            throw new BusinessException("Es gibt keinen Kontakt zum Exportieren.");
        }

        var extension = ExtensionOf(format);
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            path += extension;
        }

        var bytes = format switch
        {
            ContactExportFormat.Csv => PersonCsv.Write(people.Select(p => p.Data)),
            ContactExportFormat.VCard4 => Encoding.UTF8.GetBytes(VCard.Write(people, 4)),
            _ => Encoding.UTF8.GetBytes(VCard.Write(people, 3)),
        };

        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            Log.Info($"Exported {people.Count} contacts as {format}");
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            throw new BusinessException($"Die Datei ließ sich nicht schreiben: {exception.Message}");
        }
    }

    /// <summary>The picture of a contact from a file (≤ 5 MB, JPEG/PNG/GIF/WebP checked by the session).</summary>
    public static byte[] ReadPicture(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MatCloud.Contract.Contacts.ContactsContract.MaxPhotoBytes)
            {
                throw new BusinessException($"Ein Bild hat höchstens {MatCloud.Contract.Contacts.ContactsContract.MaxPhotoBytes / 1024 / 1024} MB.", "PhotoId");
            }

            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BusinessException($"Das Bild ließ sich nicht lesen: {exception.Message}", "PhotoId");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

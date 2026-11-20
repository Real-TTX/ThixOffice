using System.Globalization;
using DocumentFormat.OpenXml;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>Tracked changes in WordprocessingML: the w:ins / w:del / w:moveTo / w:moveFrom around runs, their dates.</summary>
internal static class DocxRevisions
{
    /// <summary>A w:date ("2026-11-10T09:00:00Z") as UTC, or null.</summary>
    public static DateTime? ParseDate(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    /// <summary>The element that holds the runs of a tracked change, with its id, author and date.</summary>
    public static OpenXmlCompositeElement Container(Revision revision, int id)
    {
        OpenXmlCompositeElement element = revision.Kind switch
        {
            RevisionKind.Deletion => new W.DeletedRun(),
            RevisionKind.MoveTo => new W.MoveToRun(),
            RevisionKind.MoveFrom => new W.MoveFromRun(),
            _ => new W.InsertedRun(),
        };
        element.SetAttribute(new OpenXmlAttribute("w", "id", DocxValues.WordNamespace, id.ToString(CultureInfo.InvariantCulture)));
        element.SetAttribute(new OpenXmlAttribute("w", "author", DocxValues.WordNamespace, string.IsNullOrEmpty(revision.Author) ? "Unbekannt" : revision.Author));
        if (revision.Date is { } date)
        {
            element.SetAttribute(new OpenXmlAttribute("w", "date", DocxValues.WordNamespace, date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        return element;
    }
}

using System.Collections.Immutable;

namespace ThixWrite.Core.Model;

/// <summary>The theme a document refers to: heading and body fonts and the colour scheme (minimal; the XML is kept by the package).</summary>
public sealed record Theme(string MajorFont, string MinorFont, ImmutableDictionary<string, string> Palette)
{
    /// <summary>The Office theme (Calibri Light / Calibri).</summary>
    public static readonly Theme Office = new(BuiltInStyles.HeadingFont, BuiltInStyles.BodyFont, new Dictionary<string, string>
    {
        ["dk1"] = "000000",
        ["lt1"] = "FFFFFF",
        ["dk2"] = "44546A",
        ["lt2"] = "E7E6E6",
        ["accent1"] = "4472C4",
        ["accent2"] = "ED7D31",
        ["accent3"] = "A5A5A5",
        ["accent4"] = "FFC000",
        ["accent5"] = "5B9BD5",
        ["accent6"] = "70AD47",
        ["hlink"] = "0563C1",
        ["folHlink"] = "954F72",
    }.ToImmutableDictionary());

    public string FontOf(ThemeFontSlot slot) => slot == ThemeFontSlot.Major ? MajorFont : MinorFont;

    /// <summary>The colour of a theme colour name as Word writes it in w:themeColor (text1 = dk1, background1 = lt1 …).</summary>
    public string? ColorOf(string themeColor)
    {
        var key = themeColor switch
        {
            "text1" or "dark1" => "dk1",
            "text2" or "dark2" => "dk2",
            "background1" or "light1" => "lt1",
            "background2" or "light2" => "lt2",
            "hyperlink" => "hlink",
            "followedHyperlink" => "folHlink",
            _ => themeColor,
        };
        return Palette.TryGetValue(key, out var value) ? value : null;
    }
}

/// <summary>Document-wide settings the model uses (the settings part itself is kept by the package).</summary>
public sealed record DocumentSettings
{
    public static readonly DocumentSettings Default = new();

    /// <summary>The distance of the default tab stops (Word: 1.25 cm).</summary>
    public double DefaultTabStop { get; init; } = 35.4;

    /// <summary>"Gerade & ungerade Seiten unterschiedlich" (w:evenAndOddHeaders): even pages show the Even headers and footers.</summary>
    public bool EvenAndOddHeaders { get; init; }
}

/// <summary>
/// Content of an opened file that ThixWrite keeps but cannot show or edit fully (plan §8: never lose content silently).
/// The UI warns with <see cref="Message"/> when a document has notes; everything listed is written back on save.
/// </summary>
public sealed record UnsupportedContent(PreservedKind Kind, int Count)
{
    /// <summary>"3 × Tabelle" – German, for the warning.</summary>
    public string Message => $"{Count} × {Label(Kind)}";

    /// <summary>The German name of a kind of content, without what happens to it (an OpenDocument file: it is not kept).</summary>
    public static string PlainLabel(PreservedKind kind) => kind switch
    {
        PreservedKind.Drawing or PreservedKind.FloatingDrawing => "Zeichnung oder Form",
        PreservedKind.Table => "Tabelle in einer Tabelle (ihr Text wird gezeigt)",
        PreservedKind.Math => "Formel",
        PreservedKind.EmbeddedObject => "Eingebettetes Objekt",
        PreservedKind.Comment => "Kommentar",
        _ => "Unbekannter Inhalt",
    };

    /// <summary>The German name of a kind of preserved content.</summary>
    public static string Label(PreservedKind kind) => kind switch
    {
        PreservedKind.Drawing => "Grafik oder Bild (Platzhalter, wird beim Speichern erhalten)",
        PreservedKind.FloatingDrawing => "Frei positioniertes Bild (wird im Textfluss angezeigt, seine Position bleibt erhalten)",
        PreservedKind.Field => "Feld (Ergebnis wird angezeigt, nicht aktualisiert)",
        PreservedKind.TrackedChange => "Nachverfolgte Änderung (angezeigt, nicht bearbeitbar)",
        PreservedKind.Comment => "Kommentar (wird erhalten, nicht angezeigt)",
        PreservedKind.Footnote => "Fußnote (wird erhalten, nicht angezeigt)",
        PreservedKind.Endnote => "Endnote (wird erhalten, nicht angezeigt)",
        PreservedKind.Math => "Formel (wird erhalten, nicht angezeigt)",
        PreservedKind.ContentControl => "Inhaltssteuerelement (angezeigt, nicht bearbeitbar)",
        PreservedKind.Table => "Verschachtelte Tabelle (angezeigt, noch nicht bearbeitbar)",
        PreservedKind.EmbeddedObject => "Eingebettetes Objekt (wird erhalten, nicht angezeigt)",
        PreservedKind.ColumnBreak => "Spaltenumbruch (wird erhalten)",
        PreservedKind.Markup => "Zusätzliche Auszeichnung (wird erhalten)",
        PreservedKind.Bookmark => "Textmarke",
        _ => "Unbekannter Inhalt (wird erhalten, nicht angezeigt)",
    };
}

/// <summary>
/// The original docx package of an opened document. The writer starts from it and replaces only the parts ThixWrite owns
/// (document body, styles, numbering), so headers, footers, images, comments, footnotes, custom XML … and every relationship id
/// inside preserved XML stay valid.
/// </summary>
public sealed class PreservedPackage(byte[] bytes)
{
    private readonly byte[] _bytes = bytes;

    /// <summary>A fresh readable copy of the original package.</summary>
    public MemoryStream Open()
    {
        var stream = new MemoryStream();
        stream.Write(_bytes);
        stream.Position = 0;
        return stream;
    }

    public int Size => _bytes.Length;
}

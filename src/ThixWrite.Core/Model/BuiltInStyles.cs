namespace ThixWrite.Core.Model;

/// <summary>
/// The built-in styles of a new document, like Word's (ids and file names in English as Word writes them, the UI shows the German
/// names): Standard, Überschrift 1–6, Titel, Untertitel, Zitat, Intensives Zitat, Listenabsatz, Kein Leerraum and the character
/// styles Absatz-Standardschriftart, Fett, Hervorhebung, Hyperlink. Styles a document lacks are added on first use
/// (<see cref="Ensure"/>).
/// </summary>
public static class BuiltInStyles
{
    public const string Normal = "Normal";
    public const string Heading1 = "Heading1";
    public const string Heading2 = "Heading2";
    public const string Heading3 = "Heading3";
    public const string Title = "Title";
    public const string Subtitle = "Subtitle";
    public const string Quote = "Quote";
    public const string IntenseQuote = "IntenseQuote";
    public const string ListParagraph = "ListParagraph";
    public const string NoSpacing = "NoSpacing";
    public const string DefaultParagraphFont = "DefaultParagraphFont";
    public const string Strong = "Strong";
    public const string Emphasis = "Emphasis";
    public const string SubtleEmphasis = "SubtleEmphasis";
    public const string IntenseEmphasis = "IntenseEmphasis";
    public const string SubtleReference = "SubtleReference";
    public const string IntenseReference = "IntenseReference";
    public const string BookTitle = "BookTitle";
    public const string HyperlinkStyle = "Hyperlink";
    public const string Header = "Header";
    public const string Footer = "Footer";
    public const string TableNormal = "TableNormal";
    public const string TableGrid = "TableGrid";
    public const string Toc1 = "TOC1";
    public const string Toc2 = "TOC2";
    public const string Toc3 = "TOC3";
    public const string TocHeading = "TOCHeading";
    public const string CommentText = "CommentText";
    public const string CommentReference = "CommentReference";

    public const string BodyFont = "Calibri";
    public const string HeadingFont = "Calibri Light";
    public const double BodySize = 11;

    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>German names of Word's built-in styles by their canonical (file) name, lower case.</summary>
    private static readonly Dictionary<string, string> GermanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Normal"] = "Standard",
        ["heading 1"] = "Überschrift 1",
        ["heading 2"] = "Überschrift 2",
        ["heading 3"] = "Überschrift 3",
        ["heading 4"] = "Überschrift 4",
        ["heading 5"] = "Überschrift 5",
        ["heading 6"] = "Überschrift 6",
        ["heading 7"] = "Überschrift 7",
        ["heading 8"] = "Überschrift 8",
        ["heading 9"] = "Überschrift 9",
        ["Title"] = "Titel",
        ["Subtitle"] = "Untertitel",
        ["Quote"] = "Zitat",
        ["Intense Quote"] = "Intensives Zitat",
        ["List Paragraph"] = "Listenabsatz",
        ["No Spacing"] = "Kein Leerraum",
        ["Default Paragraph Font"] = "Absatz-Standardschriftart",
        ["Strong"] = "Fett",
        ["Emphasis"] = "Hervorhebung",
        ["Subtle Emphasis"] = "Schwache Hervorhebung",
        ["Intense Emphasis"] = "Intensive Hervorhebung",
        ["Subtle Reference"] = "Schwacher Verweis",
        ["Intense Reference"] = "Intensiver Verweis",
        ["Book Title"] = "Buchtitel",
        ["Hyperlink"] = "Hyperlink",
        ["FollowedHyperlink"] = "Besuchter Hyperlink",
        ["Normal Table"] = "Normale Tabelle",
        ["Table Grid"] = "Tabellenraster",
        ["No List"] = "Keine Liste",
        ["caption"] = "Beschriftung",
        ["header"] = "Kopfzeile",
        ["footer"] = "Fußzeile",
        ["footnote text"] = "Fußnotentext",
        ["footnote reference"] = "Fußnotenzeichen",
        ["endnote text"] = "Endnotentext",
        ["endnote reference"] = "Endnotenzeichen",
        ["annotation text"] = "Kommentartext",
        ["annotation reference"] = "Kommentarzeichen",
        ["toc 1"] = "Verzeichnis 1",
        ["toc 2"] = "Verzeichnis 2",
        ["toc 3"] = "Verzeichnis 3",
        ["TOC Heading"] = "Inhaltsverzeichnisüberschrift",
        ["List Bullet"] = "Aufzählungszeichen",
        ["List Number"] = "Listennummer",
        ["Balloon Text"] = "Sprechblasentext",
    };

    /// <summary>The name the UI shows: the German name of a built-in style, else the name in the file.</summary>
    public static string DisplayName(Style style) => GermanNames.TryGetValue(style.Name, out var german) ? german : style.Name;

    /// <summary>True when <paramref name="name"/> is the name (in the file or in German) of one of Word's built-in styles.</summary>
    public static bool IsBuiltInName(string name) =>
        GermanNames.ContainsKey(name) || GermanNames.Values.Contains(name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>The styles of a new document, with Word's document defaults (Calibri 11, 8 pt after, 1.08 lines, German).</summary>
    /// <summary>A theme colour as Word writes it in a style: the value for older readers plus the theme name, tint or shade.</summary>
    private static TextColor Themed(string hex, string theme, string? tint = null, string? shade = null) => new(hex, theme, tint, shade);

    public static StyleSheet CreateDefault()
    {
        var characterDefaults = new CharacterProperties
        {
            FontFamily = BodyFont,
            FontTheme = ThemeFontSlot.Minor,
            FontSize = BodySize,
            UnknownXml = $"<w:lang w:val=\"de-DE\" w:eastAsia=\"en-US\" w:bidi=\"ar-SA\" xmlns:w=\"{WordNamespace}\" />",
        };
        var paragraphDefaults = new ParagraphProperties
        {
            SpacingAfter = 8,
            LineSpacing = new LineSpacing(LineSpacingRule.Multiple, 259 / 240.0),
            WidowControl = true,
        };

        string[] initial =
        [
            Normal, Heading1, Heading2, Heading3, Title, Subtitle, Quote, IntenseQuote, ListParagraph, NoSpacing,
            DefaultParagraphFont, Strong, Emphasis, HyperlinkStyle,
        ];
        return new StyleSheet(initial.Select(id => Definition(id)!), paragraphDefaults, characterDefaults);
    }

    /// <summary>The sheet with the built-in style <paramref name="id"/> (and the styles it is based on) added when missing.</summary>
    public static StyleSheet Ensure(StyleSheet sheet, string id)
    {
        if (sheet.Find(id) is not null)
        {
            return sheet;
        }

        var definition = Definition(id);
        if (definition is null)
        {
            return sheet;
        }

        var result = sheet;
        if (definition.BasedOn is not null)
        {
            result = Ensure(result, definition.BasedOn);
        }

        return result.With(definition);
    }

    /// <summary>The built-in definition of style <paramref name="id"/>, or null when it is not a built-in style.</summary>
    public static Style? Definition(string id)
    {
        if (id.StartsWith("Heading", StringComparison.Ordinal) && int.TryParse(id.AsSpan(7), out var level) && level is >= 1 and <= 9)
        {
            return HeadingStyle(level);
        }

        return id switch
        {
            Normal => new Style { Id = Normal, Name = "Normal", IsDefault = true, QuickFormat = true },
            Title => new Style
            {
                Id = Title,
                Name = "Title",
                BasedOn = Normal,
                Next = Normal,
                QuickFormat = true,
                UiPriority = 10,
                Paragraph = new ParagraphProperties
                {
                    SpacingAfter = 0,
                    LineSpacing = LineSpacing.Single,
                    ContextualSpacing = true,
                },
                Character = new CharacterProperties { FontFamily = HeadingFont, FontTheme = ThemeFontSlot.Major, FontSize = 28 },
            },
            Subtitle => new Style
            {
                Id = Subtitle,
                Name = "Subtitle",
                BasedOn = Normal,
                Next = Normal,
                QuickFormat = true,
                UiPriority = 11,
                Character = new CharacterProperties { Color = TextColor.FromHex("5A5A5A") },
            },
            Quote => new Style
            {
                Id = Quote,
                Name = "Quote",
                BasedOn = Normal,
                Next = Normal,
                QuickFormat = true,
                UiPriority = 29,
                Paragraph = new ParagraphProperties
                {
                    SpacingBefore = 10,
                    IndentLeft = 43.2,
                    IndentRight = 43.2,
                    Alignment = ParagraphAlignment.Center,
                },
                Character = new CharacterProperties { Italic = true, Color = Themed("404040", "text1", tint: "BF") },
            },
            IntenseQuote => new Style
            {
                Id = IntenseQuote,
                Name = "Intense Quote",
                BasedOn = Normal,
                Next = Normal,
                QuickFormat = true,
                UiPriority = 30,
                Paragraph = new ParagraphProperties
                {
                    SpacingBefore = 18,
                    SpacingAfter = 18,
                    IndentLeft = 43.2,
                    IndentRight = 43.2,
                    Alignment = ParagraphAlignment.Center,
                },
                Character = new CharacterProperties { Italic = true, Color = Themed("4472C4", "accent1") },
            },
            ListParagraph => new Style
            {
                Id = ListParagraph,
                Name = "List Paragraph",
                BasedOn = Normal,
                QuickFormat = true,
                UiPriority = 34,
                Paragraph = new ParagraphProperties { IndentLeft = 36, ContextualSpacing = true },
            },
            NoSpacing => new Style
            {
                Id = NoSpacing,
                Name = "No Spacing",
                QuickFormat = true,
                UiPriority = 1,
                Paragraph = new ParagraphProperties { SpacingAfter = 0, LineSpacing = LineSpacing.Single },
            },
            DefaultParagraphFont => new Style
            {
                Id = DefaultParagraphFont,
                Name = "Default Paragraph Font",
                Type = StyleType.Character,
                IsDefault = true,
                UiPriority = 1,
                SemiHidden = true,
                UnhideWhenUsed = true,
            },
            Strong => new Style
            {
                Id = Strong,
                Name = "Strong",
                Type = StyleType.Character,
                BasedOn = DefaultParagraphFont,
                QuickFormat = true,
                UiPriority = 22,
                Character = new CharacterProperties { Bold = true },
            },
            Emphasis => new Style
            {
                Id = Emphasis,
                Name = "Emphasis",
                Type = StyleType.Character,
                BasedOn = DefaultParagraphFont,
                QuickFormat = true,
                UiPriority = 20,
                Character = new CharacterProperties { Italic = true },
            },
            SubtleEmphasis or IntenseEmphasis or SubtleReference or IntenseReference or BookTitle => new Style
            {
                Id = id,
                Name = id switch
                {
                    SubtleEmphasis => "Subtle Emphasis",
                    IntenseEmphasis => "Intense Emphasis",
                    SubtleReference => "Subtle Reference",
                    IntenseReference => "Intense Reference",
                    _ => "Book Title",
                },
                Type = StyleType.Character,
                BasedOn = DefaultParagraphFont,
                QuickFormat = true,
                UiPriority = id switch { SubtleEmphasis => 19, IntenseEmphasis => 21, SubtleReference => 31, IntenseReference => 32, _ => 33 },
                Character = id switch
                {
                    SubtleEmphasis => new CharacterProperties { Italic = true, Color = TextColor.FromHex("404040") },
                    IntenseEmphasis => new CharacterProperties { Italic = true, Color = Themed("4472C4", "accent1") },
                    SubtleReference => new CharacterProperties { SmallCaps = true, Color = TextColor.FromHex("5A5A5A") },
                    IntenseReference => new CharacterProperties { Bold = true, SmallCaps = true, Color = Themed("4472C4", "accent1"), Spacing = 0.25 },
                    _ => new CharacterProperties { Bold = true, Italic = true, Spacing = 0.25 },
                },
            },
            Header or Footer => new Style
            {
                Id = id,
                Name = id == Header ? "header" : "footer",
                BasedOn = Normal,
                UiPriority = 99,
                UnhideWhenUsed = true,
                Paragraph = new ParagraphProperties
                {
                    SpacingAfter = 0,
                    LineSpacing = LineSpacing.Single,
                    Tabs = [new TabStop(226.8, TabAlignment.Center), new TabStop(453.6, TabAlignment.Right)],
                },
            },
            TableNormal => new Style
            {
                Id = TableNormal,
                Name = "Normal Table",
                Type = StyleType.Table,
                IsDefault = true,
                UiPriority = 99,
                SemiHidden = true,
                UnhideWhenUsed = true,
                Table = new TableProperties { Indent = 0, CellMargins = CellMargins.WordDefault },
            },
            TableGrid => new Style
            {
                Id = TableGrid,
                Name = "Table Grid",
                Type = StyleType.Table,
                BasedOn = TableNormal,
                UiPriority = 39,
                Paragraph = new ParagraphProperties { SpacingAfter = 0, LineSpacing = LineSpacing.Single },
                Table = new TableProperties { Borders = BorderSet.All(BorderLine.Single) },
            },
            NoteNumbering.FootnoteText or NoteNumbering.EndnoteText or CommentText => new Style
            {
                Id = id,
                Name = id == NoteNumbering.FootnoteText ? "footnote text" : id == NoteNumbering.EndnoteText ? "endnote text" : "annotation text",
                BasedOn = Normal,
                UiPriority = 99,
                SemiHidden = true,
                UnhideWhenUsed = true,
                Paragraph = new ParagraphProperties { SpacingAfter = 0, LineSpacing = LineSpacing.Single },
                Character = new CharacterProperties { FontSize = 10 },
            },
            NoteNumbering.FootnoteReference or NoteNumbering.EndnoteReference => new Style
            {
                Id = id,
                Name = id == NoteNumbering.FootnoteReference ? "footnote reference" : "endnote reference",
                Type = StyleType.Character,
                BasedOn = DefaultParagraphFont,
                UiPriority = 99,
                SemiHidden = true,
                UnhideWhenUsed = true,
                Character = new CharacterProperties { VerticalPosition = VerticalPosition.Superscript },
            },
            CommentReference => new Style
            {
                Id = id,
                Name = "annotation reference",
                Type = StyleType.Character,
                BasedOn = DefaultParagraphFont,
                UiPriority = 99,
                SemiHidden = true,
                UnhideWhenUsed = true,
                Character = new CharacterProperties { FontSize = 8 },
            },
            _ when id.StartsWith("TOC", StringComparison.Ordinal) && id.Length == 4 && char.IsDigit(id[3]) && id[3] != '0' => new Style
            {
                Id = id,
                Name = $"toc {id[^1]}",
                BasedOn = Normal,
                Next = Normal,
                UiPriority = 39,
                UnhideWhenUsed = true,
                Paragraph = new ParagraphProperties { SpacingAfter = 5, IndentLeft = (id[^1] - '1') * 11.0 },
            },
            TocHeading => new Style
            {
                Id = TocHeading,
                Name = "TOC Heading",
                BasedOn = Heading1,
                Next = Normal,
                UiPriority = 39,
                UnhideWhenUsed = true,
                QuickFormat = true,
                Paragraph = new ParagraphProperties { OutlineLevel = 9 },
            },
            HyperlinkStyle => new Style
            {
                Id = HyperlinkStyle,
                Name = "Hyperlink",
                Type = StyleType.Character,
                BasedOn = DefaultParagraphFont,
                UiPriority = 99,
                UnhideWhenUsed = true,
                Character = new CharacterProperties { Color = Themed("0563C1", "hyperlink"), Underline = UnderlineStyle.Single },
            },
            _ => null,
        };
    }

    /// <summary>The paragraph styles the ribbon's gallery shows, in Word's order.</summary>
    public static IReadOnlyList<string> GalleryOrder { get; } =
    [
        Normal, NoSpacing, Heading1, Heading2, Heading3, Title, Subtitle, SubtleEmphasis, Emphasis, IntenseEmphasis, Strong, Quote, IntenseQuote,
        SubtleReference, IntenseReference, BookTitle, ListParagraph,
    ];

    private static Style HeadingStyle(int level)
    {
        var (size, color, before) = level switch
        {
            1 => (16.0, "BF", 12.0),
            2 => (13.0, "BF", 2.0),
            3 => (12.0, "7F", 2.0),
            _ => (11.0, "BF", 2.0),
        };
        return new Style
        {
            Id = $"Heading{level}",
            Name = $"heading {level}",
            BasedOn = Normal,
            Next = Normal,
            QuickFormat = true,
            UiPriority = 9,
            Paragraph = new ParagraphProperties
            {
                KeepWithNext = true,
                KeepLinesTogether = true,
                SpacingBefore = before,
                SpacingAfter = 0,
                OutlineLevel = level - 1,
            },
            Character = new CharacterProperties
            {
                FontFamily = HeadingFont,
                FontTheme = ThemeFontSlot.Major,
                FontSize = size,
                Color = Themed(color == "BF" ? "2F5496" : "1F3763", "accent1", shade: color),
                Italic = level == 4 ? true : null,
            },
        };
    }
}

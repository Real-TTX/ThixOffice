using ThixWrite.Core.Model;

namespace ThixWrite.Core.Documents;

/// <summary>The body and heading font of new documents.</summary>
public sealed record DocumentFonts(string Body, string Heading)
{
    /// <summary>
    /// The fonts new documents prefer, in order: Aptos (Word's current default, installed with Office), Calibri (Word's
    /// long-time default, on most Windows machines), Helvetica Neue (every Mac), Arial (everywhere).
    /// </summary>
    public static IReadOnlyList<DocumentFonts> Preferred { get; } =
    [
        new("Aptos", "Aptos Display"),
        new("Calibri", "Calibri Light"),
        new("Helvetica Neue", "Helvetica Neue"),
        new("Arial", "Arial"),
    ];

    /// <summary>The first preferred pair whose body font is installed (the heading falls back to the body font), else Arial.</summary>
    public static DocumentFonts Choose(IEnumerable<string> installedFamilies)
    {
        var installed = new HashSet<string>(installedFamilies, StringComparer.OrdinalIgnoreCase);
        foreach (var fonts in Preferred)
        {
            if (installed.Contains(fonts.Body))
            {
                return installed.Contains(fonts.Heading) ? fonts : fonts with { Heading = fonts.Body };
            }
        }

        return Preferred[^1];
    }
}

/// <summary>
/// A new, empty document as ThixWrite starts it: Word's built-in styles and page (A4, margins 2.5 cm, bottom 2 cm like the
/// German Word) with <see cref="DocumentFonts"/> the machine has instead of Calibri / Calibri Light.
/// </summary>
public static class BlankDocument
{
    public static Document Create(DocumentFonts fonts)
    {
        var defaults = BuiltInStyles.CreateDefault();
        var styles = defaults.Styles.Select(style => style.Character.FontFamily switch
        {
            BuiltInStyles.BodyFont => style with { Character = style.Character with { FontFamily = fonts.Body } },
            BuiltInStyles.HeadingFont => style with { Character = style.Character with { FontFamily = fonts.Heading } },
            _ => style,
        });
        var sheet = new StyleSheet(styles, defaults.ParagraphDefaults, defaults.CharacterDefaults with { FontFamily = fonts.Body }, defaults.LatentStylesXml);
        var theme = Theme.Office with { MajorFont = fonts.Heading, MinorFont = fonts.Body };
        return new Document([Paragraph.Empty()], sheet, theme: theme);
    }
}

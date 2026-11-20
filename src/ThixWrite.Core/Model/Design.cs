using System.Collections.Immutable;

namespace ThixWrite.Core.Model;

/// <summary>A colour set of Entwurf › Farben: the twelve theme colours under a name ("Office", "Blau II" …).</summary>
public sealed record ThemeColorSet(string Name, ImmutableDictionary<string, string> Palette)
{
    private static ThemeColorSet Of(string name, string dark2, string light2, string accents, string link, string followed)
    {
        var colors = accents.Split(' ');
        var palette = new Dictionary<string, string>
        {
            ["dk1"] = "000000",
            ["lt1"] = "FFFFFF",
            ["dk2"] = dark2,
            ["lt2"] = light2,
            ["hlink"] = link,
            ["folHlink"] = followed,
        };
        for (var index = 0; index < colors.Length; index++)
        {
            palette[$"accent{index + 1}"] = colors[index];
        }

        return new ThemeColorSet(name, palette.ToImmutableDictionary());
    }

    /// <summary>Word's colour sets (the values of Office's theme files).</summary>
    public static IReadOnlyList<ThemeColorSet> All { get; } =
    [
        Of("Office", "44546A", "E7E6E6", "4472C4 ED7D31 A5A5A5 FFC000 5B9BD5 70AD47", "0563C1", "954F72"),
        Of("Office 2007 - 2010", "1F497D", "EEECE1", "4F81BD C0504D 9BBB59 8064A2 4BACC6 F79646", "0000FF", "800080"),
        Of("Graustufen", "000000", "F8F8F8", "DDDDDD B2B2B2 969696 808080 5F5F5F 4D4D4D", "5F5F5F", "919191"),
        Of("Blau", "17406D", "DBEFF9", "0F6FC6 009DD9 0BD0D9 10CF9B 7CCA62 A5C249", "F49100", "85DFD0"),
        Of("Blau II", "335B74", "DFE3E5", "1CADE4 2683C6 27CED7 42BA97 3E8853 62A39F", "6B9F25", "B26B02"),
        Of("Blaugrün", "373545", "CEDBE6", "3494BA 58B6C0 75BDA7 7A8C8E 84ACB6 2683C6", "6B9F25", "9F6715"),
        Of("Grün", "455F51", "E3DED1", "549E39 8AB833 C0CF3A 029676 4AB5C4 0989B1", "6B9F25", "BA6906"),
        Of("Gelb", "39302A", "E5DEDB", "FFCA08 F8931D CE8D3E EC7016 E64823 9C6A6A", "2998E3", "7F723D"),
        Of("Orange", "637052", "CCDDEA", "E48312 BD582C 865640 9B8357 C2BC80 94A088", "2998E3", "8C8C8C"),
        Of("Rot", "323232", "E5C243", "A5300F D55816 E19825 B19C7D 7F5F52 B27D49", "6B9F25", "B26B02"),
        Of("Violett", "373545", "DCD8DC", "AD84C6 8784C7 5D739A 6997AF 84ACB6 6F8183", "69A020", "8C8C8C"),
        Of("Papier", "444D26", "FEFAC9", "A5B592 F3A447 E7BC29 D092A7 9C85C0 809EC2", "8E58B6", "7F6F6F"),
    ];

    /// <summary>The set with this name, or null.</summary>
    public static ThemeColorSet? Find(string name) => All.FirstOrDefault(set => set.Name == name);
}

/// <summary>A font set of Entwurf › Schriftarten: heading and body font under a name.</summary>
public sealed record ThemeFontSet(string Name, string Heading, string Body)
{
    /// <summary>Word's font sets (most used).</summary>
    public static IReadOnlyList<ThemeFontSet> All { get; } =
    [
        new("Office", "Calibri Light", "Calibri"),
        new("Office 2024", "Aptos Display", "Aptos"),
        new("Office 2007 - 2010", "Cambria", "Calibri"),
        new("Calibri", "Calibri", "Calibri"),
        new("Arial", "Arial", "Arial"),
        new("Corbel", "Corbel", "Corbel"),
        new("Candara", "Candara", "Candara"),
        new("Franklin Gothic", "Franklin Gothic Medium", "Franklin Gothic Book"),
        new("Century Gothic", "Century Gothic", "Century Gothic"),
        new("Tw Cen MT", "Tw Cen MT", "Tw Cen MT"),
        new("Cambria", "Cambria", "Cambria"),
        new("Georgia", "Georgia", "Georgia"),
        new("Garamond", "Garamond", "Garamond"),
        new("Times New Roman", "Times New Roman", "Times New Roman"),
        new("Trebuchet MS", "Trebuchet MS", "Trebuchet MS"),
        new("Verdana", "Verdana", "Verdana"),
    ];

    public static ThemeFontSet? Find(string name) => All.FirstOrDefault(set => set.Name == name);
}

/// <summary>A design of Entwurf › Designs: a colour set with a font set.</summary>
public sealed record DocumentDesign(string Name, ThemeColorSet Colors, ThemeFontSet Fonts)
{
    private static DocumentDesign Of(string name, string colors, string fonts) => new(name, ThemeColorSet.Find(colors)!, ThemeFontSet.Find(fonts)!);

    public static IReadOnlyList<DocumentDesign> All { get; } =
    [
        Of("Office", "Office", "Office"),
        Of("Office 2007 - 2010", "Office 2007 - 2010", "Office 2007 - 2010"),
        Of("Facette", "Grün", "Trebuchet MS"),
        Of("Ion", "Blaugrün", "Century Gothic"),
        Of("Integral", "Blau II", "Tw Cen MT"),
        Of("Retrospektive", "Orange", "Calibri"),
        Of("Galerie", "Rot", "Georgia"),
        Of("Schiefer", "Graustufen", "Franklin Gothic"),
        Of("Papier", "Papier", "Garamond"),
        Of("Violett", "Violett", "Candara"),
    ];

    public static DocumentDesign? Find(string name) => All.FirstOrDefault(design => design.Name == name);
}

/// <summary>
/// Entwurf › Wasserzeichen: a text (Word's "VERTRAULICH", "ENTWURF" … or one of one's own) in a font and colour, diagonal or
/// horizontal, half transparent or solid – or a picture, washed out, at a scale (0 = as large as fits the text area).
/// Written into every header of the document like Word (VML in a w:pict: PowerPlusWaterMarkObject / WordPictureWatermark).
/// </summary>
public sealed record Watermark
{
    public string? Text { get; init; }

    public string FontFamily { get; init; } = "Calibri";

    /// <summary>"RRGGBB"; Word's default is silver (C0C0C0).</summary>
    public string Color { get; init; } = "C0C0C0";

    public bool Diagonal { get; init; } = true;

    public bool SemiTransparent { get; init; } = true;

    public ImageData? Picture { get; init; }

    /// <summary>The picture's scale (1 = 100 %); 0 = "Automatisch" (as large as fits).</summary>
    public double Scale { get; init; }

    public bool Washout { get; init; } = true;

    /// <summary>The texts of Word's gallery.</summary>
    public static IReadOnlyList<string> Texts { get; } =
        ["ENTWURF", "VERTRAULICH", "NICHT KOPIEREN", "MUSTER", "DRINGEND", "SO SCHNELL WIE MÖGLICH", "STRENG VERTRAULICH", "PERSÖNLICH"];
}

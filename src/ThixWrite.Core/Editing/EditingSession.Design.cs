using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>Entwurf: Designs, Farben, Schriftarten (the document's theme – styles that name theme colours and fonts follow) and Wasserzeichen.</summary>
public sealed partial class EditingSession
{
    /// <summary>Entwurf › Designs: the design's colours and fonts become the document's theme (one undo step).</summary>
    public void ApplyDesign(DocumentDesign design) =>
        ChangeTheme(Document.Theme.WithColors(design.Colors).WithFonts(design.Fonts), $"Design {design.Name}");

    /// <summary>Entwurf › Farben.</summary>
    public void SetThemeColors(ThemeColorSet colors) => ChangeTheme(Document.Theme.WithColors(colors), "Designfarben");

    /// <summary>Entwurf › Schriftarten.</summary>
    public void SetThemeFonts(ThemeFontSet fonts) => ChangeTheme(Document.Theme.WithFonts(fonts), "Designschriftarten");

    /// <summary>The colour set the document's theme uses (by its colours), or null for colours of its own.</summary>
    public ThemeColorSet? CurrentThemeColors =>
        ThemeColorSet.All.FirstOrDefault(set => set.Palette.All(pair => Document.Theme.ColorOf(pair.Key) is { } value && string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase)));

    /// <summary>The font set the document's theme uses, or null.</summary>
    public ThemeFontSet? CurrentThemeFonts =>
        ThemeFontSet.All.FirstOrDefault(set => set.Heading == Document.Theme.MajorFont && set.Body == Document.Theme.MinorFont);

    private void ChangeTheme(Theme theme, string label)
    {
        if (theme.ContentEquals(Document.Theme))
        {
            return;
        }

        Run(label, EditKind.Other, null, () => Apply(DocumentSteps.Theme(Document, theme)));
    }

    /// <summary>The document's watermark as Entwurf › Wasserzeichen shows it (the first one in a header), or null.</summary>
    public Watermark? CurrentWatermark
    {
        get
        {
            foreach (var inline in Document.Sections.SelectMany(section => HeaderStories(section.Properties)).SelectMany(Inlines))
            {
                switch (inline)
                {
                    case OpaqueInline { Graphic.Elements: [DrawingWordArt art, ..] } when DocxWatermarks.IsTextWatermark(inline):
                        return new Watermark
                        {
                            Text = art.Text,
                            FontFamily = art.FontFamily,
                            Color = art.Color.Rgb ?? "C0C0C0",
                            Diagonal = art.Rotation != 0,
                            SemiTransparent = art.Opacity < 1,
                        };
                    case ImageInline { IsWatermark: true } picture:
                        var (naturalWidth, _) = picture.Image.NaturalSize;
                        return new Watermark { Picture = picture.Image, Washout = picture.Washout, Scale = naturalWidth > 0 ? picture.Width / naturalWidth : 0 };
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Entwurf › Wasserzeichen: puts <paramref name="watermark"/> into every header of the document (the first section gets a header
    /// where it has none – also for a different first page and even pages), replacing a watermark there; null removes it ("Wasserzeichen
    /// entfernen"). One undo step.
    /// </summary>
    public void SetWatermark(Watermark? watermark)
    {
        if (watermark is { Picture: null, Text: null or "" })
        {
            return;
        }

        var sections = Document.Sections;
        Run(watermark is null ? "Wasserzeichen entfernen" : "Wasserzeichen", EditKind.Other, null, () =>
        {
            var number = 0;
            for (var index = 0; index < sections.Count; index++)
            {
                var properties = sections[index].Properties;
                var headers = properties.Headers;
                foreach (var kind in new[] { HeaderFooterKind.Default, HeaderFooterKind.First, HeaderFooterKind.Even })
                {
                    var story = headers[kind];
                    var needed = index == 0 && watermark is not null && (kind == HeaderFooterKind.Default
                        || kind == HeaderFooterKind.First && properties.TitlePage
                        || kind == HeaderFooterKind.Even && Document.Settings.EvenAndOddHeaders);
                    if (story is null && !needed)
                    {
                        continue;
                    }

                    var mark = watermark is null ? null
                        : watermark.Picture is not null ? (Inline)DocxWatermarks.PictureInline(watermark, properties.ContentWidth, properties.PageHeight - properties.MarginTop - properties.MarginBottom)
                        : DocxWatermarks.TextInline(watermark, properties.ContentWidth, ++number);
                    var changed = WithWatermark(story ?? NewHeaderFooter(footer: false), mark);
                    if (story is null || !changed.Equals(story))
                    {
                        headers = headers.With(kind, changed);
                    }
                }

                if (headers != properties.Headers)
                {
                    var updated = headers;
                    ChangeSection(index, "Wasserzeichen", section => section with { Headers = updated });
                }
            }
        });
    }

    private static IEnumerable<Story> HeaderStories(SectionProperties properties) =>
        new[] { properties.Headers.Default, properties.Headers.First, properties.Headers.Even }.OfType<Story>();

    private static IEnumerable<Inline> Inlines(Story story) => story.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines);

    /// <summary>The story without its watermarks, with <paramref name="mark"/> at the start of its first paragraph outside tables.</summary>
    private static Story WithWatermark(Story story, Inline? mark)
    {
        var blocks = story.Blocks
            .Select(block => block is Paragraph paragraph && paragraph.Inlines.Any(DocxWatermarks.IsWatermark)
                ? paragraph.WithInlines(paragraph.Inlines.Where(inline => !DocxWatermarks.IsWatermark(inline)))
                : block)
            .ToList();
        if (mark is not null)
        {
            var first = blocks.FindIndex(block => block is Paragraph { Cell: null });
            var paragraph = (Paragraph)blocks[first];
            blocks[first] = paragraph.WithInlines([mark, .. paragraph.Inlines]);
        }

        return new Story(blocks, story.SourcePart);
    }
}

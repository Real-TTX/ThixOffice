using ThixWrite.Core.Documents;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using ThixWrite.Core.Tests.Images;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Editing;

/// <summary>Entwurf: Designs, Farben, Schriftarten and Wasserzeichen.</summary>
public sealed class DesignTests
{
    private static EditingSession NewSession()
    {
        var document = BlankDocument.Create(new DocumentFonts("Calibri", "Calibri Light"));
        var session = new EditingSession(document);
        session.InsertText("Überschrift");
        session.ApplyStyle(BuiltInStyles.Heading1);
        return session;
    }

    [Fact]
    public void Theme_colours_follow_Words_tint_and_shade()
    {
        Assert.Equal("2F5496", StyleResolver.ThemedColor("4472C4", null, "BF"));
        Assert.Equal("203864", StyleResolver.ThemedColor("4472C4", null, "7F")); // Word writes 1F3763: one step apart
        Assert.Equal("404040", StyleResolver.ThemedColor("000000", "BF", null));
        Assert.Equal("4472C4", StyleResolver.ThemedColor("4472C4", null, null));
    }

    [Fact]
    public void A_design_changes_the_headings_colour_and_fonts_and_undo_brings_them_back()
    {
        var session = NewSession();
        var resolver = new StyleResolver(session.Document);
        var before = resolver.ResolveCharacter(session.Document.ParagraphAt(0)!, CharacterProperties.Empty);
        Assert.Equal("2F5496", before.Color);
        Assert.Equal("Calibri Light", before.FontFamily);

        session.ApplyDesign(DocumentDesign.Find("Facette")!);
        var after = new StyleResolver(session.Document).ResolveCharacter(session.Document.ParagraphAt(0)!, CharacterProperties.Empty);
        Assert.Equal(StyleResolver.ThemedColor("549E39", null, "BF"), after.Color);
        Assert.Equal("Trebuchet MS", after.FontFamily);
        Assert.Equal("Grün", session.CurrentThemeColors?.Name);
        Assert.Equal("Trebuchet MS", session.CurrentThemeFonts?.Name);

        session.Undo();
        Assert.Equal("2F5496", new StyleResolver(session.Document).ResolveCharacter(session.Document.ParagraphAt(0)!, CharacterProperties.Empty).Color);
    }

    [Fact]
    public void The_theme_is_written_into_the_theme_part_and_read_back()
    {
        var session = NewSession();
        session.SetThemeColors(ThemeColorSet.Find("Blau II")!);
        session.SetThemeFonts(ThemeFontSet.Find("Georgia")!);
        var written = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(written);

        var reread = DocxTestSupport.Read(written);
        Assert.Equal("1CADE4", reread.Theme.ColorOf("accent1"));
        Assert.Equal("Georgia", reread.Theme.MajorFont);
        Assert.Equal("Blau II", reread.Theme.ColorsName);

        // A file with a theme part: only the colours and fonts change.
        var again = new EditingSession(reread);
        again.SetThemeColors(ThemeColorSet.Find("Rot")!);
        var second = DocxTestSupport.Read(DocxTestSupport.Write(again.Document));
        Assert.Equal("A5300F", second.Theme.ColorOf("accent1"));
        Assert.Equal("Georgia", second.Theme.MinorFont);
    }

    [Fact]
    public void A_text_watermark_goes_into_the_header_shows_on_every_page_and_survives_a_round_trip()
    {
        var session = NewSession();
        session.SetWatermark(new Watermark { Text = "VERTRAULICH" });
        Assert.Equal("VERTRAULICH", session.CurrentWatermark?.Text);
        using (var layout = NewLayout(session.Document))
        {
            var placed = Assert.Single(layout.Pages[0].HeaderFooterFloats);
            var setup = session.Document.FinalSection;
            Assert.Equal(setup.MarginLeft + setup.ContentWidth / 2, placed.X + placed.Width / 2, 1);
            Assert.Contains(layout.GetDisplayList(0).Items, item => item is ThixOffice.Text.Drawing.GlyphRunItem);
        }

        var written = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(written);
        var reread = new EditingSession(DocxTestSupport.Read(written));
        var watermark = reread.CurrentWatermark;
        Assert.Equal("VERTRAULICH", watermark?.Text);
        Assert.True(watermark!.Diagonal);
        Assert.True(watermark.SemiTransparent);

        reread.SetWatermark(watermark with { Text = "ENTWURF", Diagonal = false });
        Assert.Equal("ENTWURF", reread.CurrentWatermark?.Text);
        Assert.False(reread.CurrentWatermark!.Diagonal);
        Assert.Single(reread.Document.FinalSection.Headers.Default!.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines),
            inline => inline is OpaqueInline { Kind: PreservedKind.Drawing });

        reread.SetWatermark(null);
        Assert.Null(reread.CurrentWatermark);
        reread.Undo();
        Assert.Equal("ENTWURF", reread.CurrentWatermark?.Text);
    }

    [Fact]
    public void A_picture_watermark_is_washed_out_behind_the_text_and_survives_a_round_trip()
    {
        var session = NewSession();
        var image = ImageData.FromBytes(ImageTests.Png(40, 20))!;
        session.SetWatermark(new Watermark { Picture = image, Scale = 2 });
        var current = session.CurrentWatermark;
        Assert.Equal(image, current?.Picture);
        Assert.True(current!.Washout);

        var written = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(written);
        var reread = new EditingSession(DocxTestSupport.Read(written));
        var picture = reread.Document.FinalSection.Headers.Default!.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<ImageInline>().Single();
        Assert.True(picture.IsWatermark);
        Assert.True(picture.Washout);
        Assert.True(picture.Anchor!.BehindText);
        Assert.Equal(reread.CurrentWatermark?.Picture, image);
    }
}

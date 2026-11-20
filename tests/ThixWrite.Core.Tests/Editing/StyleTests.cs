using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Editing;

/// <summary>Formatvorlagen: the gallery, changing a style, new styles, a style from the selection.</summary>
public sealed class StyleTests
{
    private static EditingSession NewSession(params string[] paragraphs) => new(new Document(paragraphs.Select(text => new Paragraph(text)), BuiltInStyles.CreateDefault()));

    [Fact]
    public void The_gallery_has_Words_quick_styles_in_Words_order()
    {
        var session = NewSession("Text");
        var names = session.GalleryStyles().Select(BuiltInStyles.DisplayName).ToList();
        Assert.Equal(["Standard", "Kein Leerraum", "Überschrift 1", "Überschrift 2", "Überschrift 3", "Titel", "Untertitel", "Schwache Hervorhebung", "Hervorhebung",
            "Intensive Hervorhebung", "Fett", "Zitat", "Intensives Zitat", "Schwacher Verweis", "Intensiver Verweis", "Buchtitel", "Listenabsatz"], names);
    }

    [Fact]
    public void Changing_a_style_changes_every_paragraph_that_uses_it_and_is_one_undo_step()
    {
        var session = NewSession("Eins", "Zwei", "Drei");
        session.SetCaret(At(0, 0));
        session.ApplyStyle(BuiltInStyles.Heading1);
        session.SetCaret(At(2, 0));
        session.ApplyStyle(BuiltInStyles.Heading1);
        var heading = session.Document.Styles.Find(BuiltInStyles.Heading1)!;

        session.ModifyStyle(heading with { Character = heading.Character with { FontSize = 20, Bold = true } });
        var resolver = session.Resolver;
        foreach (var block in new[] { 0, 2 })
        {
            var paragraph = session.Document.ParagraphAt(block)!;
            var format = resolver.ResolveCharacter(paragraph, ((TextRun)paragraph.Inlines[0]).Properties);
            Assert.Equal(20, format.FontSize);
            Assert.True(format.Bold);
        }

        session.Undo();
        var first = session.Document.ParagraphAt(0)!;
        Assert.Equal(16, session.Resolver.ResolveCharacter(first, ((TextRun)first.Inlines[0]).Properties).FontSize);
    }

    [Fact]
    public void A_new_style_formats_the_selected_paragraphs_and_survives_the_docx_round_trip()
    {
        var session = NewSession("Hinweis", "Normal");
        session.SetCaret(At(0, 0));
        Assert.NotNull(session.StyleNameProblem(""));
        Assert.NotNull(session.StyleNameProblem("Überschrift 1"));
        Assert.Null(session.StyleNameProblem("Hinweiskasten"));

        var id = session.CreateStyle("Hinweiskasten", StyleType.Paragraph, BuiltInStyles.Normal, null,
            new ParagraphProperties { IndentLeft = 36 }, new CharacterProperties { Color = TextColor.FromHex("C00000") });
        Assert.Equal("Hinweiskasten", id);
        Assert.Equal(id, session.Document.ParagraphAt(0)!.Properties.StyleId);
        Assert.Null(session.Document.ParagraphAt(1)!.Properties.StyleId);
        Assert.NotNull(session.StyleNameProblem("Hinweiskasten")); // taken now
        Assert.Contains(session.GalleryStyles(), style => style.Id == id);

        var reread = DocxTestSupport.RoundTrip(session.Document);
        var style = reread.Styles.Find(id)!;
        Assert.Equal("Hinweiskasten", style.Name);
        Assert.Equal(36, style.Paragraph.IndentLeft);
        Assert.Equal("C00000", style.Character.Color!.Value);
        Assert.Equal(id, style.Next);
        session.Undo();
        Assert.Null(session.Document.Styles.Find(id));
    }

    [Fact]
    public void A_style_from_the_selection_takes_its_format_and_is_based_on_the_paragraphs_style()
    {
        var session = NewSession("Wichtig");
        session.SelectAll();
        session.FormatCharacters("Fett", (_, props) => props with { Bold = true, FontSize = 14 });
        session.FormatParagraphs("Zentriert", props => props with { Alignment = ParagraphAlignment.Center });

        var id = session.StyleFromSelection("Wichtig");
        var style = session.Document.Styles.Find(id)!;
        Assert.Equal(BuiltInStyles.Normal, style.BasedOn);
        Assert.True(style.Character.Bold);
        Assert.Equal(14, style.Character.FontSize);
        Assert.Equal(ParagraphAlignment.Center, style.Paragraph.Alignment);
        Assert.Equal(id, session.Document.ParagraphAt(0)!.Properties.StyleId);
    }
}

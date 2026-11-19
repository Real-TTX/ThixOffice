using ThixOffice.Text;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Tests;

/// <summary>Small builders for documents, sessions and layouts in tests.</summary>
internal static class TestDocuments
{
    public static Document Create(params string[] paragraphs) => new(paragraphs.Select(text => new Paragraph(text)));

    public static Document Create(IEnumerable<Block> blocks) => new(blocks);

    public static EditingSession Session(Document document, Func<DateTime>? clock = null) => new(document, clock);

    public static EditingSession Session(params string[] paragraphs) => new(Create(paragraphs));

    public static DocumentLayout NewLayout(Document document, LayoutOptions? options = null) => new(document, new FixedWidthTextMeasurer(), options);

    /// <summary>A session with its layout as navigator.</summary>
    public static (EditingSession Session, DocumentLayout Layout) Editor(Document document)
    {
        var session = new EditingSession(document);
        var layout = new DocumentLayout(document, new FixedWidthTextMeasurer(), resolver: session.Resolver);
        session.Navigator = layout;
        return (session, layout);
    }

    public static DocumentPosition At(int block, int offset) => new(block, offset);

    public static string Texts(Document document) => string.Join("|", document.Blocks.Select(block => block is Paragraph paragraph ? paragraph.Text : "[opaque]"));

    /// <summary>A long text of words, deterministic.</summary>
    public static string Words(int count, int seed = 1)
    {
        string[] words = ["Lorem", "ipsum", "dolor", "sit", "amet", "Textverarbeitung", "über", "Größe", "und", "Absatz", "Seite", "ThixWrite"];
        var random = new Random(seed);
        return string.Join(' ', Enumerable.Range(0, count).Select(_ => words[random.Next(words.Length)]));
    }

    /// <summary>Checks the model invariants a correct edit keeps.</summary>
    public static void AssertInvariants(EditingSession session)
    {
        var document = session.Document;
        Assert.NotEmpty(document.Blocks);
        Assert.IsType<Paragraph>(document.Blocks[^1]);
        Assert.Null(((Paragraph)document.Blocks[^1]).Cell);
        foreach (var table in TableSpan.All(document))
        {
            Assert.Empty(table.Problems(document));
        }

        Assert.DoesNotContain(document.Blocks, block => block is Paragraph { Cell: not null, SectionBreak: not null });
        foreach (var paragraph in document.Blocks.OfType<Paragraph>())
        {
            Assert.DoesNotContain(paragraph.Inlines, inline => inline is TextRun { Length: 0 });
            for (var index = 1; index < paragraph.Inlines.Length; index++)
            {
                if (paragraph.Inlines[index - 1] is TextRun previous && paragraph.Inlines[index] is TextRun current)
                {
                    Assert.False(previous.CanJoin(current), "Adjacent runs with the same formatting must be joined.");
                }
            }

            Assert.Equal(paragraph.Text.Length, paragraph.Inlines.Sum(inline => inline.Length));
        }

        foreach (var position in new[] { session.Selection.Anchor, session.Selection.Active })
        {
            Assert.InRange(position.Block, 0, document.Blocks.Count - 1);
            Assert.InRange(position.Offset, 0, document.Blocks[position.Block].Length);
            Assert.False(document.ParagraphAt(position.Block)?.Cell?.IsCovered == true, $"The selection is in a hidden block {position.Block}.");
        }
    }
}

using System.Diagnostics;
using ThixOffice.Text;
using ThixOffice.Text.Skia;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using Xunit.Abstractions;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Layouts;

public class IncrementalLayoutTests(ITestOutputHelper output)
{
    /// <summary>About 300 pages: 3000 paragraphs of 60 words (5 lines each) with headings every 20 paragraphs, a list in between.</summary>
    internal static Document LongDocument()
    {
        var blocks = new List<Block>();
        for (var index = 0; index < 3000; index++)
        {
            if (index % 20 == 0)
            {
                blocks.Add(new Paragraph($"Kapitel {index / 20 + 1}", new ParagraphProperties { StyleId = BuiltInStyles.Heading1 }));
                continue;
            }

            blocks.Add(new Paragraph(Words(60, seed: index)));
        }

        return new Document(blocks);
    }

    [Fact]
    public void Typing_re_lays_one_paragraph_and_matches_a_full_layout()
    {
        var document = LongDocument();
        var (session, layout) = Editor(document);
        using var _ = layout;
        layout.Update();

        session.SetCaret(new DocumentPosition(1500, 10));
        session.InsertText("x");
        layout.Update();

        Assert.Equal(1, layout.LastUpdate.ParagraphsLaidOut);
        Assert.True(layout.LastUpdate.FirstPage > 100);
        Editing.RandomEditTests.AssertSameLayout(layout, document);
    }

    [Fact]
    public void Splitting_merging_and_list_changes_stay_equal_to_a_full_layout()
    {
        var document = LongDocument();
        var (session, layout) = Editor(document);
        using var _ = layout;
        layout.Update();

        session.SetCaret(new DocumentPosition(700, 30));
        session.InsertParagraphBreak();
        layout.Update();
        Assert.True(layout.LastUpdate.ParagraphsLaidOut <= 2);
        Editing.RandomEditTests.AssertSameLayout(layout, document);

        session.DeleteBackward();
        Editing.RandomEditTests.AssertSameLayout(layout, document);

        session.Select(new DocumentPosition(901, 0), new DocumentPosition(905, 0));
        session.ToggleList(ListKind.Numbered);
        Editing.RandomEditTests.AssertSameLayout(layout, document);

        session.SetCaret(new DocumentPosition(902, 0));
        session.InsertParagraphBreak();
        Editing.RandomEditTests.AssertSameLayout(layout, document);

        session.ApplyStyle(BuiltInStyles.Title);
        Editing.RandomEditTests.AssertSameLayout(layout, document);

        session.Undo();
        session.Undo();
        session.Undo();
        Editing.RandomEditTests.AssertSameLayout(layout, document);
    }

    [Fact]
    public void Changing_a_style_lays_out_everything_again()
    {
        var document = Create("eins", "zwei");
        var (session, layout) = Editor(document);
        using var _ = layout;
        layout.Update();

        session.SelectAll();
        session.ApplyStyle(BuiltInStyles.Quote);
        layout.Update();

        Assert.Equal(2, layout.LastUpdate.ParagraphsLaidOut);
    }

    [Fact]
    public void The_layout_raises_changed_with_the_first_changed_page()
    {
        var document = LongDocument();
        var (session, layout) = Editor(document);
        using var _ = layout;
        layout.Update();
        LayoutChangedEventArgs? changed = null;
        layout.Changed += (_, args) => changed = args;

        session.SetCaret(new DocumentPosition(2900, 0));
        session.InsertText("x");
        layout.Update();

        Assert.NotNull(changed);
        Assert.Equal(layout.PageOf(new DocumentPosition(2900, 0)), changed.FirstChangedPage);
        Assert.Equal(layout.PageCount, changed.PageCount);
    }

    [Fact]
    public void Performance_of_a_300_page_document()
    {
        Measure("fixed-width measurer", new FixedWidthTextMeasurer(), typingBudgetMs: 25);
        using var skia = new SkiaTextMeasurer();
        Measure("Skia/HarfBuzz measurer", skia, typingBudgetMs: 50);
    }

    private void Measure(string name, ITextMeasurer measurer, double typingBudgetMs)
    {
        var document = LongDocument();
        var session = new EditingSession(document);
        using var layout = new DocumentLayout(document, measurer, resolver: session.Resolver);
        session.Navigator = layout;

        var watch = Stopwatch.StartNew();
        layout.Update();
        var initial = watch.Elapsed;
        Assert.InRange(layout.PageCount, 250, 450);

        // Type 200 characters in the middle, laying out after every key like the editor does.
        session.SetCaret(new DocumentPosition(1500, 20));
        var times = new List<double>();
        for (var index = 0; index < 200; index++)
        {
            watch.Restart();
            session.InsertText(index % 6 == 5 ? " " : "a");
            layout.Update();
            _ = session.GetFormatting();
            _ = layout.GetCaret(new CaretPlace(session.Selection.Active));
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        // Enter and Backspace (structure changes) at the start of the document re-paginate everything after them.
        session.SetCaret(new DocumentPosition(5, 10));
        var structural = new List<double>();
        for (var index = 0; index < 20; index++)
        {
            watch.Restart();
            if (index % 2 == 0)
            {
                session.InsertParagraphBreak();
            }
            else
            {
                session.DeleteBackward();
            }

            layout.Update();
            structural.Add(watch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        structural.Sort();
        var median = times[times.Count / 2];
        var p95 = times[(int)(times.Count * 0.95)];
        output.WriteLine($"{name}: {document.Blocks.Count} paragraphs, {layout.PageCount} pages; first layout {initial.TotalMilliseconds:0} ms; " +
            $"typing: median {median:0.00} ms, 95% {p95:0.00} ms, max {times[^1]:0.00} ms; Enter/Backspace near the start: median {structural[structural.Count / 2]:0.00} ms, max {structural[^1]:0.00} ms");

        Assert.True(median < typingBudgetMs, $"{name}: typing median {median:0.00} ms");
        Assert.True(structural[structural.Count / 2] < typingBudgetMs * 2, $"{name}: Enter/Backspace median {structural[structural.Count / 2]:0.00} ms");
    }
}

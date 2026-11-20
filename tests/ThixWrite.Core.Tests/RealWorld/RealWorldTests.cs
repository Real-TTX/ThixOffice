using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;

namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>
/// Regression facts of the real-world corpus: page counts, where things are, how they look (resolved formats, cell fills, text
/// box rectangles, columns), and the docx round trip of every file (re-read equals, unknown parts kept). Laid out with the
/// deterministic fixed-width measurer, so the numbers do not depend on the fonts of the machine.
/// </summary>
public sealed class RealWorldTests
{
    public static TheoryData<string> Files =>
    [
        .. Directory.GetFiles(Corpus.Folder, "*.docx", SearchOption.AllDirectories).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal),
    ];

    private static (Document Document, DocumentLayout Layout) Open(string name)
    {
        var document = DocxReader.Read(new MemoryStream(Corpus.Bytes(name)));
        var layout = new DocumentLayout(document, new FixedWidthTextMeasurer());
        layout.Update();
        return (document, layout);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_file_opens_lays_out_and_survives_a_round_trip(string name)
    {
        var bytes = Corpus.Bytes(name);
        var document = DocxReader.Read(new MemoryStream(bytes));
        using (var layout = new DocumentLayout(document, new FixedWidthTextMeasurer()))
        {
            layout.Update();
            Assert.True(layout.PageCount >= 1);
        }

        var written = DocxTestSupport.Write(document);
        var reread = DocxTestSupport.Read(written);
        DocxTestSupport.AssertSameContent(document, reread);
        Assert.Equal(PartNames(bytes), PartNames(written)); // no part lost (pictures, charts, SmartArt, comments …)
    }

    private static IReadOnlyList<string> PartNames(byte[] bytes)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        return [.. zip.Entries.Select(entry => entry.FullName).Where(entry => !entry.EndsWith(".rels", StringComparison.Ordinal) && !entry.StartsWith("docProps/", StringComparison.Ordinal)
                                                                              && entry is not "[Content_Types].xml" and not "word/webSettings.xml" and not "word/fontTable.xml").Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void Table_styles_give_the_heading_row_its_fill_and_white_bold_text_and_the_rows_their_bands()
    {
        var (document, layout) = Open("07-demo-tables.docx");
        var cells = layout.Pages[0].Cells;
        var first = cells.Where(cell => cell.TableStart == cells[0].TableStart).ToList();
        Assert.Equal("4472C4", first.Single(cell => cell.Row == 0 && cell.Cell == 1).Fill); // Überschrift: accent 1
        Assert.Equal(WordStyles.Tint("4472C4", 0.2), first.Single(cell => cell.Row == 1 && cell.Cell == 1).Fill); // band 1: the accent at 20 %
        Assert.Null(first.Single(cell => cell.Row == 2 && cell.Cell == 1).Fill); // band 2: none
        Assert.False(first.Single(cell => cell.Row == 0 && cell.Cell == 1).Left?.IsVisible ?? false); // no inside lines in the heading row

        var resolver = new StyleResolver(document);
        var header = document.Blocks.OfType<Paragraph>().First(paragraph => paragraph.Text == "2024");
        var format = resolver.ResolveCharacter(header, ((TextRun)header.Inlines[0]).Properties);
        Assert.True(format.Bold);
        Assert.Equal("FFFFFF", format.Color);
        var nord = document.Blocks.OfType<Paragraph>().First(paragraph => paragraph.Text == "Nord");
        Assert.True(resolver.ResolveCharacter(nord, ((TextRun)nord.Inlines[0]).Properties).Bold); // erste Spalte
        var value = document.Blocks.OfType<Paragraph>().First(paragraph => paragraph.Text == "4,3");
        Assert.False(resolver.ResolveCharacter(value, ((TextRun)value.Inlines[0]).Properties).Bold);
    }

    private const double PointsPerCentimetre = 72 / 2.54;

    [Fact]
    public void Floating_pictures_shapes_and_text_boxes_sit_at_their_anchors_and_the_text_flows_around_them()
    {
        var (document, layout) = Open("02-demo-floating.docx");
        Assert.Empty(document.UnsupportedContent);
        var floats = layout.Pages[0].Floats;
        var sidebar = floats.Single(placed => placed.Inline is OpaqueInline { Graphic.Elements: [DrawingShape { Text: not null }] } && placed.Anchor.HorizontalFrame == DrawingFrame.Page);
        Assert.Equal(14.5 * PointsPerCentimetre, sidebar.X, 1);
        Assert.Equal(19 * PointsPerCentimetre, sidebar.Y, 1);
        Assert.Equal(5.5 * PointsPerCentimetre, sidebar.Width, 1);

        // The sidebar is placed on the page: the text before its anchor paragraph flows around it too.
        var besideSidebar = layout.Pages[0].Lines.Where(line => line.Top + line.Line.Height > sidebar.Y + 1 && line.Top < sidebar.Y + sidebar.Height).ToList();
        Assert.Contains(besideSidebar, line => line.Block < sidebar.Block);
        Assert.All(besideSidebar, line => Assert.True(line.Left + line.Line.Items.Select(item => item.X + item.Width).DefaultIfEmpty(0).Max() <= sidebar.X - sidebar.Anchor.DistanceLeft + 3));

        // The square-wrapped photo on the right: the first paragraph's lines end before it.
        var photo = floats.First(placed => placed.Inline is ImageInline);
        Assert.Equal(DrawingWrap.Square, photo.Anchor.Wrap);
        var section = document.FinalSection;
        Assert.Equal(section.MarginLeft + section.ContentWidth, photo.X + photo.Width, 1); // aligned right in the column
        var beside = layout.Pages[0].Lines.Where(line => line.Block == 1 && line.Top + 1 < photo.Y + photo.Height).ToList();
        Assert.NotEmpty(beside);
        Assert.All(beside, line => Assert.True(line.Left + line.Line.Items.Max(item => item.X + item.Width) <= photo.X - photo.Anchor.DistanceLeft + 3)); // a trailing space may hang

        // A group keeps its children in their places; the star is a star.
        Assert.Contains(floats, placed => placed.Inline is OpaqueInline { Graphic.Elements.Count: 3 });
        Assert.Contains(floats, placed => placed.Inline is OpaqueInline { Graphic.Elements: [DrawingShape { Preset: "star5" }] });
    }

    [Fact]
    public void A_cover_page_in_a_content_control_opens_as_its_paragraphs_with_its_shapes_on_the_page()
    {
        var (document, layout) = Open("01-demo-cover-toc.docx");
        var first = (Paragraph)document.Blocks[0];
        Assert.IsType<ContentControlMarkInline>(first.Inlines[0]);
        var background = layout.Pages[0].Floats.Single(placed => placed.Anchor.BehindText);
        Assert.Equal((0, 0), (background.X, background.Y));
        Assert.Equal(document.FinalSection.PageWidth, background.Width, 0);
        var title = layout.Pages[0].Floats.Single(placed => !placed.Anchor.BehindText);
        var shape = Assert.IsType<DrawingShape>(Assert.IsType<OpaqueInline>(title.Inline).Graphic!.Elements[0]);
        Assert.Contains("JAHRESBERICHT 2026", ((Paragraph)shape.Text!.Blocks[0]).Text);
        Assert.Equal(DrawingTextAnchor.Bottom, shape.TextAnchor);
    }

    [Fact]
    public void Charts_and_SmartArt_are_drawn_from_the_file()
    {
        var (document, layout) = Open("03-demo-chart-smartart.docx");
        Assert.DoesNotContain(document.UnsupportedContent, note => note.Kind == PreservedKind.Drawing);
        var drawings = document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<OpaqueInline>().Where(inline => inline.Graphic is not null).ToList();
        Assert.Contains(drawings, drawing => drawing.Graphic!.Elements is [DrawingChart]);
        var smartArt = drawings.Single(drawing => drawing.Graphic!.Elements.Count == 5);
        Assert.Equal(3, smartArt.Graphic!.Elements.OfType<DrawingShape>().Count(shape => shape.Preset == "roundRect"));
        var list = layout.GetDisplayList(0);
        Assert.Contains(list.Items, item => item is GlyphRunItem { Text: "Q3" }); // the category axis of the chart
    }

    [Fact]
    public void The_letter_has_its_logo_in_the_header_and_the_address_field_where_DIN_5008_puts_it()
    {
        var (_, layout) = Open("09-letter-din5008.docx");
        var page = layout.Pages[0];
        var logo = Assert.Single(page.HeaderFooterFloats);
        Assert.Equal(14 * PointsPerCentimetre, logo.X, 1);
        var address = page.Floats.Single(placed => placed.Inline is OpaqueInline { Graphic.Elements: [DrawingShape { Text: { } text }] } && ((Paragraph)text.Blocks[0]).Text.StartsWith("Nordlicht", StringComparison.Ordinal));
        Assert.Equal((2.5 * PointsPerCentimetre, 4.5 * PointsPerCentimetre), (Math.Round(address.X, 1), Math.Round(address.Y, 1)), new ToleranceComparer(0.2));
    }

    [Fact]
    public void Columns_flow_balance_before_a_continuous_break_and_have_their_line()
    {
        var (document, layout) = Open("04-demo-columns-sections.docx");
        var page = layout.Pages[0];
        var twoColumns = document.Sections[1];
        var lefts = page.Lines.Where(line => line.Block >= twoColumns.StartBlock && line.Block <= twoColumns.EndBlock).Select(line => Math.Round(line.Left)).Distinct().ToList();
        Assert.Equal(2, lefts.Count);
        var bottoms = lefts.Select(left => page.Lines.Where(line => Math.Round(line.Left) == left && line.Block <= twoColumns.EndBlock && line.Block >= twoColumns.StartBlock).Max(line => line.Top + line.Line.Height)).ToList();
        Assert.True(Math.Abs(bottoms[0] - bottoms[1]) < 40, $"balanced: {bottoms[0]} / {bottoms[1]}");
        Assert.Equal(2, page.ColumnLines.Count); // three columns with "Zwischenlinie"

        var (_, newsletter) = Open("14-newsletter.docx");
        Assert.Equal(3, newsletter.Pages[0].Lines.Where(line => line.Block > 1).Select(line => Math.Round(line.Left)).Distinct().Count());
    }

    [Fact]
    public void Character_and_paragraph_formats_page_colour_and_drop_caps_are_drawn()
    {
        var (document, layout) = Open("05-demo-typography.docx");
        var list = layout.GetDisplayList(0);
        var background = Assert.IsType<RectangleItem>(list.Items[0]);
        Assert.Equal(RgbColor.FromHex("FBF8EF"), background.Fill); // Seitenfarbe
        var texts = list.Items.OfType<GlyphRunItem>().Select(run => run.Text).ToList();
        Assert.DoesNotContain(texts, text => text.Contains("verborgen", StringComparison.Ordinal)); // hidden text
        Assert.Contains("GROSSBUCHSTABEN".Replace("SS", "ß", StringComparison.Ordinal), texts); // caps, the ß stays
        Assert.Contains(list.Items, item => item is RectangleItem { Fill: { } fill } && fill == RgbColor.FromHex("E8F1F8")); // paragraph shading
        var resolver = new StyleResolver(document);
        var spaced = document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<TextRun>().First(run => run.Text == "gesperrt");
        Assert.Equal(3, spaced.Properties.Spacing);

        // The drop cap: the paragraph after it starts beside the letter.
        var (_, newsletter) = Open("14-newsletter.docx");
        var cap = newsletter.Pages[0].Floats.Single(placed => placed.Inline is TextRun);
        var next = newsletter.Pages[0].Lines.First(line => line.Block == cap.Block + 1);
        Assert.True(next.Left + next.Line.Items[0].X >= cap.X + cap.Width);
    }

    [Fact]
    public void Nested_tables_rows_after_a_gap_turned_text_and_floating_tables_are_laid_out_like_Word()
    {
        var (document, layout) = Open("07-demo-tables.docx");
        Assert.DoesNotContain(document.Blocks, block => block is OpaqueBlock); // no grey frames left
        var blocks = document.Blocks.ToList();
        int BlockOf(string text) => blocks.FindIndex(block => block is Paragraph paragraph && paragraph.Text == text);
        IEnumerable<(PageLayout Page, PlacedLine Line)> LinesOf(int block) => layout.Pages.SelectMany(page => page.Lines.Where(line => line.Block == block).Select(line => (page, line)));

        // Nested: the inner table rides in a paragraph of the outer cell and draws its cells.
        var carrier = blocks.FindIndex(block => block is Paragraph { Inlines: [OpaqueInline { Nested: not null }] });
        Assert.True(carrier > 0);
        Assert.True(LinesOf(carrier).Single().Line.Line.Height > 10);

        // w:gridBefore: the row's cell starts one grid column (2000 twips = 100 pt) to the right of the first row's.
        var gap = BlockOf("nach Lücke");
        var gapPage = LinesOf(gap).First().Page;
        var gapCell = ((Paragraph)blocks[gap]).Cell!.Value;
        var tableStart = TableSpan.At(document, gap)!.Start;
        var firstCell = gapPage.Cells.First(cell => cell.TableStart == tableStart && cell.Row == 0 && cell.Cell == 0);
        var shifted = gapPage.Cells.First(cell => cell.TableStart == tableStart && cell.Row == gapCell.Row);
        Assert.Equal(firstCell.X + 100, shifted.X, 1);
        Assert.True(shifted.Left?.IsVisible); // its own outer edge

        // Turned text ("btLr"): drawn through a transform inside its cell.
        var turned = LinesOf(BlockOf("gedreht")).Select(item => item.Line).ToList();
        Assert.NotEmpty(turned);
        Assert.All(turned, line => Assert.NotNull(line.Transform));

        // The floating table sits at the right margin and the paragraph after it flows beside it.
        var floating = BlockOf("Schwebende Tabelle rechts");
        var (floatPage, floatLine) = LinesOf(floating).Single();
        var section = document.FinalSection;
        var floatCell = floatPage.Cells.Single(cell => cell.TableStart == floating && cell.Row == 0);
        Assert.Equal(section.MarginLeft + section.ContentWidth, floatCell.X + floatCell.Width, 1);
        var beside = floatPage.Lines.Where(line => line.Block == floating + 2 && line.Top < floatCell.Y + floatCell.Height).ToList();
        Assert.NotEmpty(beside);
        Assert.All(beside, line => Assert.True(line.Left + line.Line.Items.Max(item => item.X + item.Width) <= floatCell.X + 3));
        _ = floatLine;
    }

    [Fact]
    public void Formulas_are_set_as_formulas_display_ones_centred_and_kept_as_they_were()
    {
        var (document, layout) = Open("15-formulas.docx");
        var formulas = document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<OpaqueInline>().Where(inline => inline.Math is not null).ToList();
        Assert.Equal(8, formulas.Count);
        Assert.Equal(5, formulas.Count(formula => formula.IsDisplayMath));

        // The trees: the quadratic formula is x = a fraction with a radical; the sum has its limits under and over it.
        var quadratic = Assert.IsType<MathRow>(formulas[0].Math);
        var fraction = Assert.IsType<MathFraction>(quadratic.Items[^1]);
        Assert.Contains(((MathRow)fraction.Numerator).Items, item => item is MathRadical { Degree: null });
        var sum = Assert.IsType<MathNary>(((MathRow)formulas[1].Math!).Items[0]);
        Assert.True(sum.Limits);
        Assert.Equal("∑", sum.Operator);
        Assert.IsType<MathMatrix>(((MathDelimiter)((MathRow)formulas[3].Math!).Items[^1]).Items[0] is MathRow { Items: [var matrix] } ? matrix : null);

        // Laid out: a display formula is a line of its own, taller than text, its box the width of the line (it is centred in it).
        var page = layout.Pages[0];
        var body = page.Lines[1].Line.Height;
        var display = page.Lines.Single(line => line.Line.Items.Any(item => item is ObjectItem { Inline: var inline } && ReferenceEquals(inline, formulas[0])));
        Assert.True(display.Line.Height > body * 1.8, $"{display.Line.Height} vs {body}");
        var item = display.Line.Items.OfType<ObjectItem>().Single();
        Assert.Equal(document.FinalSection.ContentWidth, item.Width, 1);

        // Drawn: the operators and the fraction bar.
        var list = layout.GetDisplayList(0);
        Assert.Contains(list.Items, drawn => drawn is GlyphRunItem { Text: "∑" });
        Assert.Contains(list.Items, drawn => drawn is LineItem { Y1: var y1, Y2: var y2 } && Math.Abs(y1 - y2) < 0.01 && y1 > display.Top && y1 < display.Top + display.Line.Height);
        Assert.Contains(document.UnsupportedContent, note => note.Kind == PreservedKind.Math); // read-only: the opening note says so
    }

    [Fact]
    public void Content_controls_in_the_line_and_tracked_changes_are_text_with_their_own_formats()
    {
        var (document, layout) = Open("08-demo-review.docx");
        Assert.DoesNotContain(document.UnsupportedContent, note => note.Kind is PreservedKind.ContentControl or PreservedKind.TrackedChange);
        var paragraphs = document.Blocks.OfType<Paragraph>().ToList();

        // "Name: [Anna Berger]" – the control's text is a bold run between two marks.
        var name = paragraphs.Single(paragraph => paragraph.Text.Contains("Anna Berger", StringComparison.Ordinal) && paragraph.Text.StartsWith("Name", StringComparison.Ordinal));
        var marks = name.Inlines.OfType<ContentControlMarkInline>().ToList();
        Assert.Equal([false, true], marks.Select(mark => mark.IsEnd));
        Assert.All(marks, mark => Assert.True(mark.IsInline));
        Assert.True(name.Inlines.OfType<TextRun>().Single(run => run.Text == "Anna Berger").Properties.Bold);

        // Tracked changes: the inserted text is a run naming its insertion; the deleted text is there, named, and not shown.
        var changed = paragraphs.Single(paragraph => paragraph.Inlines.Any(inline => inline.Revision is { Kind: RevisionKind.Insertion }) && paragraph.Text.StartsWith("Ein Satz", StringComparison.Ordinal));
        Assert.Equal("Anna Berger", changed.Inlines.First(inline => inline.Revision is not null).Revision!.Author);
        var block = paragraphs.IndexOf(changed);
        var shown = string.Concat(layout.GetDisplayList(0).Items.OfType<GlyphRunItem>().Select(run => run.Text));
        Assert.Contains("eingefügtem", shown);
        Assert.DoesNotContain("gelöschtem", shown);
        Assert.True(block >= 0);
    }

    [Fact]
    public void A_footer_with_a_table_is_laid_out_as_a_table()
    {
        var (_, layout) = Open("09-letter-din5008.docx");
        var cells = layout.Pages[0].HeaderFooterCells;
        Assert.Equal(3, cells.Count);
        Assert.True(cells[1].X > cells[0].X && cells[2].X > cells[1].X);
        Assert.All(cells, cell => Assert.True(cell.Y > layout.Pages[0].Height / 2));
    }

    private sealed class ToleranceComparer(double tolerance) : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) x, (double, double) y) => Math.Abs(x.Item1 - y.Item1) <= tolerance && Math.Abs(x.Item2 - y.Item2) <= tolerance;

        public int GetHashCode((double, double) obj) => 0;
    }
}

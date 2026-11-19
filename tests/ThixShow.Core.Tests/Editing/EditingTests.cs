using ThixShow.Core.Editing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Editing;

public class EditingTests
{
    private sealed class Clock
    {
        public DateTime Now { get; set; } = new(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
    }

    private static (EditingSession Session, Clock Clock) NewSession(Presentation? presentation = null)
    {
        var clock = new Clock();
        return (new EditingSession(presentation ?? TestDecks.Deck(3), () => clock.Now), clock);
    }

    private static readonly EmuRect Box = new(914400, 914400, 2743200, 914400);

    [Fact]
    public void Typing_groups_into_one_undo_step_until_a_pause()
    {
        var (session, clock) = NewSession();
        var original = session.Presentation;
        var id = session.InsertTextBox(Box);
        session.InsertText("Hallo");
        clock.Now += TimeSpan.FromSeconds(1);
        session.InsertText(" Welt");
        Assert.Equal("Hallo Welt", ((AutoShape)session.CurrentSlide!.FindShape(id)!).Text!.PlainText);
        session.Undo();
        Assert.Equal("", ((AutoShape)session.CurrentSlide!.FindShape(id)!).Text!.PlainText);
        session.Redo();
        clock.Now += TimeSpan.FromSeconds(5);
        session.InsertText("!");
        session.Undo();
        Assert.Equal("Hallo Welt", ((AutoShape)session.CurrentSlide!.FindShape(id)!).Text!.PlainText);
        session.Undo();
        session.Undo();
        Assert.Same(original, session.Presentation);
        Assert.Equal("Textfeld einfügen", session.RedoLabel);
    }

    [Fact]
    public void Paragraphs_split_join_and_carry_their_properties()
    {
        var (session, _) = NewSession();
        var id = session.InsertTextBox(Box, "Eins");
        session.SetAlignment(TextAlignment.Center);
        session.InsertParagraphBreak();
        session.InsertText("Zwei");
        var text = ((AutoShape)session.CurrentSlide!.FindShape(id)!).Text!;
        Assert.Equal("Eins\nZwei", text.PlainText);
        Assert.All(text.Paragraphs, paragraph => Assert.Equal(TextAlignment.Center, paragraph.Properties.Alignment));
        session.SetCaret(new TextTarget(id), new TextPosition(1, 0));
        session.DeleteBackward();
        Assert.Equal("EinsZwei", ((AutoShape)session.CurrentSlide!.FindShape(id)!).Text!.PlainText);
        session.InsertLineBreak();
        Assert.Equal("Eins\vZwei", ((AutoShape)session.CurrentSlide!.FindShape(id)!).Text!.PlainText);
        session.MoveCaret(CaretMovement.TextEnd);
        session.DeleteBackward(word: true);
        Assert.Equal("Eins\v", ((AutoShape)session.CurrentSlide!.FindShape(id)!).Text!.PlainText);
    }

    [Fact]
    public void Formatting_part_of_a_run_splits_it_and_the_query_reports_mixed_values()
    {
        var (session, _) = NewSession();
        var id = session.InsertTextBox(Box, "Hallo Welt");
        var target = new TextTarget(id);
        session.SelectText(target, new TextPosition(0, 0), new TextPosition(0, 5));
        session.ToggleBold();
        Assert.True(session.GetFormatting().Bold.Is(true));
        var runs = ((AutoShape)session.CurrentSlide!.FindShape(id)!).Text!.Paragraphs[0].Runs;
        Assert.Equal(2, runs.Count);
        Assert.Equal("Hallo", runs[0].Text);
        Assert.True(runs[0].Properties.Bold);
        session.SelectAllText();
        Assert.True(session.GetFormatting().Bold.IsMixed);
        session.ToggleBold();
        Assert.True(session.GetFormatting().Bold.Is(true));
        Assert.Single(((AutoShape)session.CurrentSlide!.FindShape(id)!).Text!.Paragraphs[0].Runs);
        session.SetFontSize(32);
        Assert.Equal(32, session.GetFormatting().FontSize.Value);
        session.GrowFont();
        Assert.Equal(36, session.GetFormatting().FontSize.Value);

        // A caret alone sets what the next typing gets.
        session.SetCaret(target, new TextPosition(0, 10));
        session.SetItalic(true);
        session.InsertText("!");
        var last = ((AutoShape)session.CurrentSlide!.FindShape(id)!).Text!.Paragraphs[0].Runs[^1];
        Assert.Equal("!", last.Text);
        Assert.True(last.Properties.Italic);
    }

    [Fact]
    public void Levels_bullets_and_numbering_change_the_selected_paragraphs()
    {
        var (session, _) = NewSession();
        var slide = session.CurrentSlide!;
        var body = slide.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.Type == PlaceholderType.Object);
        var target = new TextTarget(body.Id);
        session.SelectText(target, new TextPosition(1, 0), new TextPosition(1, 0));
        session.HandleTab(shift: false);
        Assert.Equal(1, ((AutoShape)session.CurrentSlide!.FindShape(body.Id)!).Text!.Paragraphs[1].Properties.Level);
        Assert.True(session.GetFormatting().BulletKind.Is("bullet"));
        session.ToggleNumbering();
        Assert.True(session.GetFormatting().BulletKind.Is("number"));
        session.ToggleNumbering();
        Assert.True(session.GetFormatting().BulletKind.Is("none"));
        session.HandleTab(shift: true);
        Assert.Null(((AutoShape)session.CurrentSlide!.FindShape(body.Id)!).Text!.Paragraphs[1].Properties.Level);
    }

    [Fact]
    public void Slides_are_added_duplicated_moved_hidden_and_deleted()
    {
        var (session, _) = NewSession();
        var ids = session.Presentation.Slides.Select(slide => slide.Id).ToList();
        var added = session.AddSlide();
        Assert.Equal(added, session.Presentation.Slides[1].Id);
        Assert.Equal(added, session.Selection.CurrentSlide);
        Assert.Equal("obj", session.Presentation.LayoutOf(session.Presentation.Slides[1]).Type);
        var copies = session.DuplicateSlides([ids[0]]);
        Assert.Equal(copies[0], session.Presentation.Slides[1].Id);
        Assert.Equal(session.Presentation.Slides[0].Shapes.Count, session.Presentation.Slides[1].Shapes.Count);
        session.MoveSlides([ids[0]], 5);
        Assert.Equal(ids[0], session.Presentation.Slides[^1].Id);
        session.SetSlidesHidden([ids[1]], true);
        Assert.True(session.Presentation.FindSlide(ids[1])!.Hidden);
        session.DeleteSlides([ids[1], ids[2]]);
        Assert.Equal(3, session.Presentation.SlideCount);
        session.DeleteSlides(session.Presentation.Slides.Select(slide => slide.Id).ToList());
        Assert.Equal(3, session.Presentation.SlideCount); // never the last one
    }

    [Fact]
    public void Changing_the_layout_keeps_the_content_and_reset_brings_placeholders_back()
    {
        var (session, _) = NewSession();
        var slide = session.CurrentSlide!;
        var twoObj = session.Presentation.Masters[0].Layouts.First(layout => layout.Type == "twoObj");
        session.ChangeLayout([slide.Id], twoObj.Id);
        var changed = session.CurrentSlide!;
        Assert.Equal(twoObj.Id, changed.LayoutId);
        Assert.Contains(changed.Shapes.OfType<AutoShape>(), shape => shape.Text?.PlainText.StartsWith("Erster Punkt", StringComparison.Ordinal) == true && shape.Placeholder is not null);
        Assert.Equal(2, changed.Shapes.Count(shape => shape.Placeholder?.Type == PlaceholderType.Object));
        var titleOnly = session.Presentation.Masters[0].Layouts.First(layout => layout.Type == "titleOnly");
        session.ChangeLayout([slide.Id], titleOnly.Id);
        Assert.Contains(session.CurrentSlide!.Shapes.OfType<AutoShape>(), shape => shape.Placeholder is null && shape.Text?.PlainText.StartsWith("Erster Punkt", StringComparison.Ordinal) == true);

        var moved = session.CurrentSlide!.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.IsTitle == true);
        session.SelectShapes([moved.Id]);
        session.MoveShapes(12700, 0);
        Assert.NotNull(((AutoShape)session.CurrentSlide!.FindShape(moved.Id)!).Transform);
        session.ResetSlides([slide.Id]);
        Assert.Null(((AutoShape)session.CurrentSlide!.FindShape(moved.Id)!).Transform);
    }

    [Fact]
    public void Sections_group_slides()
    {
        var (session, _) = NewSession(TestDecks.Deck(4));
        var ids = session.Presentation.Slides.Select(slide => slide.Id).ToList();
        var second = session.AddSection("Hauptteil", ids[2]);
        Assert.Equal(2, session.Presentation.Sections.Count);
        Assert.Equal([ids[0], ids[1]], session.Presentation.Sections[0].SlideIds.ToList());
        Assert.Equal([ids[2], ids[3]], session.Presentation.Sections[1].SlideIds.ToList());
        session.RenameSection(second, "Mitte");
        Assert.Equal("Mitte", session.Presentation.Sections[1].Name);
        session.MoveSection(second, 0);
        Assert.Equal([ids[2], ids[3], ids[0], ids[1]], session.Presentation.Slides.Select(slide => slide.Id).ToList());
        var added = session.AddSlide();
        Assert.Contains(added, session.Presentation.SectionOf(added)!.SlideIds);
        session.MoveSlidesToSection([ids[2]], session.Presentation.Sections[1].Id);
        Assert.Equal(session.Presentation.Sections[1].Id, session.Presentation.SectionOf(ids[2])!.Id);
        session.RemoveSection(session.Presentation.Sections[1].Id);
        Assert.Single(session.Presentation.Sections);
        Assert.Equal(5, session.Presentation.Sections[0].SlideIds.Count);
    }

    [Fact]
    public void Shapes_are_inserted_transformed_arranged_grouped_and_ungrouped()
    {
        var (session, _) = NewSession(TestDecks.Blank());
        var first = session.InsertShape("rect", new EmuRect(0, 0, 1270000, 1270000));
        var second = session.InsertShape("ellipse", new EmuRect(2540000, 1270000, 1270000, 635000));
        session.SelectShapes([first, second]);
        session.Align(AlignKind.Top);
        Assert.Equal(0, session.BoundsOf(second)!.Value.Y);
        session.Align(AlignKind.Center, toSlide: true);
        Assert.Equal(session.Presentation.SlideSize.Width / 2, session.BoundsOf(first)!.Value.CenterX);
        session.SelectShapes([second]);
        session.Arrange(ZOrderMove.SendToBack);
        Assert.Equal(second, session.CurrentSlide!.Shapes[0].Id);
        session.Rotate(90);
        session.Flip(horizontal: true);
        var transform = session.CurrentSlide!.FindShape(second)!.Transform!;
        Assert.True(transform.FlipHorizontal);
        Assert.Equal(270 * 60000, transform.Rotation);

        var before = SlideHitTesting.CornersOf(session.Presentation, session.CurrentSlide!, second);
        session.SelectShapes([first, second]);
        var group = session.GroupShapes()!.Value;
        Assert.IsType<GroupShape>(session.CurrentSlide!.FindShape(group));
        Assert.Single(session.CurrentSlide!.Shapes);
        session.SetBounds(group, new EmuRect(0, 0, session.BoundsOf(group)!.Value.Width * 2, session.BoundsOf(group)!.Value.Height));
        var stretched = SlideHitTesting.CornersOf(session.Presentation, session.CurrentSlide!, second);
        session.Ungroup();
        var after = SlideHitTesting.CornersOf(session.Presentation, session.CurrentSlide!, second);
        Assert.Equal(2, session.CurrentSlide!.Shapes.Count);
        for (var index = 0; index < 4; index++)
        {
            Assert.Equal(stretched[index].X, after[index].X, 1);
            Assert.Equal(stretched[index].Y, after[index].Y, 1);
        }

        Assert.NotEqual(before[0].X, after[0].X, 1);
        session.SelectShapes([first]);
        var copies = session.DuplicateShapes();
        Assert.Equal(session.BoundsOf(first)!.Value.X + EditingSession.DuplicateOffset, session.BoundsOf(copies[0])!.Value.X);
        session.DeleteShapes();
        Assert.Null(session.CurrentSlide!.FindShape(copies[0]));
    }

    [Fact]
    public void Distribute_spaces_shapes_evenly()
    {
        var (session, _) = NewSession(TestDecks.Blank());
        var a = session.InsertShape("rect", new EmuRect(0, 0, 100000, 100000));
        var b = session.InsertShape("rect", new EmuRect(150000, 0, 100000, 100000));
        var c = session.InsertShape("rect", new EmuRect(900000, 0, 100000, 100000));
        session.SelectShapes([a, b, c]);
        session.Distribute(horizontal: true);
        Assert.Equal(450000, session.BoundsOf(b)!.Value.X);
    }

    [Fact]
    public void Adjust_handles_change_the_geometry()
    {
        var (session, _) = NewSession(TestDecks.Blank());
        var id = session.InsertShape("roundRect", new EmuRect(0, 0, 2540000, 1270000));
        session.DragAdjustHandle(id, 0, 508000, 0);
        var adjust = ((AutoShape)session.CurrentSlide!.FindShape(id)!).Geometry!.AdjustOf("adj")!.Value;
        Assert.InRange(adjust, 39990, 40010);
    }

    [Fact]
    public void Moving_snaps_to_other_shapes_the_slide_centre_and_the_grid()
    {
        var presentation = TestDecks.Blank(TestDecks.Rectangle(10, 1000000, 1000000, 1000000, 500000, "accent1"));
        var slide = presentation.Slides[0];
        var result = Snapping.Snap(presentation, slide, new EmuRect(1030000, 3000000, 400000, 400000), [], threshold: 50000);
        Assert.Equal(-30000, result.Dx);
        Assert.Contains(result.Guides, guide => guide.Vertical && guide.Position == 1000000);
        var centre = Snapping.Snap(presentation, slide, new EmuRect(5900000, 3200000, 400000, 400000), [], threshold: 50000);
        Assert.Equal(6096000 - 6100000, centre.Dx);
        var grid = Snapping.Snap(presentation, slide, new EmuRect(5000010, 5000010, 10, 10), [], threshold: 0, grid: Snapping.DefaultGrid);
        Assert.Equal(0, (5000010 + grid.Dx) % Snapping.DefaultGrid);
    }

    [Fact]
    public void Tables_get_rows_columns_and_merged_cells()
    {
        var (session, _) = NewSession(TestDecks.Blank());
        var id = session.InsertTable(2, 2);
        Table TableOf() => ((GraphicFrame)session.CurrentSlide!.FindShape(id)!).Table!;
        session.SelectText(new TextTarget(id, 0, 0), new TextPosition(0, 0), new TextPosition(0, 0));
        session.InsertText("A1");
        session.InsertTableRow(id, 0, below: true);
        session.InsertTableColumn(id, 1, right: true);
        Assert.Equal(3, TableOf().RowCount);
        Assert.Equal(3, TableOf().ColumnCount);
        Assert.Equal(TableOf().ColumnWidths.Sum(), session.CurrentSlide!.FindShape(id)!.Transform!.Width);
        session.MergeTableCells(id, 0, 0, 1, 1);
        Assert.Equal(2, TableOf().CellAt(0, 0).GridSpan);
        Assert.Equal(2, TableOf().CellAt(0, 0).RowSpan);
        Assert.True(TableOf().CellAt(1, 1).VerticalMerge);
        Assert.Equal("A1", TableOf().CellAt(0, 0).Text.PlainText);
        session.SplitTableCell(id, 0, 0);
        Assert.False(TableOf().CellAt(1, 1).IsCovered);
        session.DeleteTableColumns(id, 0, 1);
        session.DeleteTableRows(id, 0, 1);
        Assert.Equal(2, TableOf().RowCount);
        Assert.Equal(2, TableOf().ColumnCount);
    }

    [Fact]
    public void Design_size_transitions_animations_and_notes()
    {
        var (session, _) = NewSession();
        session.ApplyDesign(BuiltInThemes.Abend);
        Assert.Equal("Abend", session.Presentation.Masters[0].Theme.Name);
        Assert.All(session.Presentation.Slides, slide => Assert.NotNull(session.Presentation.FindLayout(slide.LayoutId)));

        var title = session.CurrentSlide!.Shapes.First(shape => shape.Placeholder?.IsTitle == true);
        session.SelectShapes([title.Id]);
        session.MoveShapes(0, 0);
        var rectangle = session.CurrentSlide!.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder is null);
        var titleSize = session.Presentation.Masters[0].TextStyles.Title.LevelOf(0)!.DefaultRun!.Size!.Value;
        session.SetSlideSize(SlideSize.Standard, SlideScaleMode.Fit);
        var scaled = session.CurrentSlide!.FindShape(rectangle.Id)!.Transform!;
        Assert.Equal((long)Math.Round(rectangle.Transform!.Width * (9144000 / 12192000.0)), scaled.Width);
        // "Passend skalieren" shrinks the text with the content (the master's title size here).
        Assert.Equal((int)Math.Round(titleSize * (9144000 / 12192000.0)), session.Presentation.Masters[0].TextStyles.Title.LevelOf(0)!.DefaultRun!.Size);

        session.SetTransition([session.CurrentSlide!.Id], new SlideTransition(TransitionKind.Fade));
        session.ApplyTransitionToAll();
        Assert.All(session.Presentation.Slides, slide => Assert.Equal(TransitionKind.Fade, slide.Transition!.Kind));

        session.SelectShapes([rectangle.Id]);
        session.AddAnimation(AnimationPreset.FlyIn, AnimationDirection.Left);
        session.AddAnimation(AnimationPreset.Spin, trigger: AnimationTrigger.AfterPrevious);
        session.MoveAnimation(1, 0);
        session.SetAnimationTiming(0, AnimationTrigger.WithPrevious, 300, 1000);
        var effects = session.CurrentSlide!.Timing!.MainSequence;
        Assert.Equal(AnimationClass.Emphasis, effects[0].Class);
        Assert.Equal(1000, effects[0].DurationMs);
        Assert.Equal(300, effects[0].DelayMs);
        session.DeleteShapes([rectangle.Id]);
        Assert.Null(session.CurrentSlide!.Timing);

        session.SetNotesText(session.CurrentSlide!.Id, "Zuerst danken\nDann Zahlen");
        Assert.Equal("Zuerst danken\nDann Zahlen", session.CurrentSlide!.NotesText);
    }

    [Fact]
    public void Clipboard_fragments_carry_shapes_slides_and_text()
    {
        var (session, _) = NewSession(TestDecks.Blank());
        var id = session.InsertShape("ellipse", new EmuRect(0, 0, 1270000, 1270000));
        session.SelectShapes([id]);
        var shapes = session.Copy()!;
        Assert.Equal(FragmentKind.Shapes, shapes.Kind);
        session.Paste(shapes);
        Assert.Equal(2, session.CurrentSlide!.Shapes.Count);
        Assert.Equal(EditingSession.DuplicateOffset, session.BoundsOf(session.Selection.Shapes[0])!.Value.X);

        var bytes = shapes.ToBytes();
        var read = ShowFragment.FromBytes(bytes)!;
        Assert.Equal(FragmentKind.Shapes, read.Kind);
        session.Paste(read);
        Assert.Equal(3, session.CurrentSlide!.Shapes.Count);
        Assert.Null(ShowFragment.FromBytes([1, 2, 3]));

        var box = session.InsertTextBox(Box, "Kopierter Text");
        session.SelectText(new TextTarget(box), new TextPosition(0, 0), new TextPosition(0, 9));
        var text = session.Copy()!;
        Assert.Equal(FragmentKind.Text, text.Kind);
        Assert.Equal("Kopierter", text.PlainText);
        session.SetCaret(new TextTarget(box), new TextPosition(0, 14));
        session.Paste(text);
        Assert.Equal("Kopierter TextKopierter", ((AutoShape)session.CurrentSlide!.FindShape(box)!).Text!.PlainText);

        session.EndTextEditing();
        session.SelectSlides([session.CurrentSlide!.Id]);
        session.SelectShapes([]);
        var slides = session.Copy()!;
        Assert.Equal(FragmentKind.Slides, slides.Kind);
        session.Paste(slides);
        Assert.Equal(2, session.Presentation.SlideCount);
        Assert.NotEqual(session.Presentation.Slides[0].Id, session.Presentation.Slides[1].Id);
    }

    [Fact]
    public void Find_and_replace_go_across_slides()
    {
        var (session, _) = NewSession(TestDecks.Deck(3));
        var options = new FindOptions("punkt");
        Assert.True(session.FindNext(options));
        var first = session.Selection.CurrentSlide;
        Assert.True(session.FindNext(options));
        Assert.True(session.FindNext(options));
        Assert.True(session.FindNext(options));
        Assert.NotEqual(first, session.Selection.CurrentSlide);
        Assert.False(session.FindNext(new FindOptions("nirgends")));
        Assert.Equal(9, session.ReplaceAll(options, "Gedanke"));
        Assert.DoesNotContain(session.Presentation.Slides.SelectMany(slide => slide.Shapes).OfType<AutoShape>(), shape => shape.Text?.PlainText.Contains("Punkt", StringComparison.Ordinal) == true);
        session.Undo();
        Assert.Equal(9, session.ReplaceAll(new FindOptions("Punkt", MatchCase: true, WholeWords: true), "Gedanke"));
    }

    [Fact]
    public void Change_events_name_the_changed_slides_and_shapes()
    {
        var (session, _) = NewSession();
        PresentationChangedEventArgs? last = null;
        session.PresentationChanged += (_, e) => last = e;
        var slideId = session.CurrentSlide!.Id;
        var shape = session.CurrentSlide!.Shapes.OfType<AutoShape>().First(item => item.Placeholder is null);
        session.SelectShapes([shape.Id]);
        session.MoveShapes(12700, 0);
        Assert.Equal([slideId], last!.ChangedSlides);
        Assert.Equal([shape.Id], last.ChangedShapes[slideId]);
        Assert.False(last.SlideListChanged);
        session.AddSlide();
        Assert.True(last.SlideListChanged);
        session.ApplyDesign(BuiltInThemes.Klar);
        Assert.True(last.DesignChanged);
    }

    [Fact]
    public void A_group_of_commands_is_one_undo_step()
    {
        var (session, _) = NewSession(TestDecks.Blank());
        var original = session.Presentation;
        session.Group("Mehrere", () =>
        {
            session.InsertShape("rect", Box);
            session.InsertShape("ellipse", Box);
            session.SetFill(new SolidFill(ColorSpec.Rgb("FF0000")));
        });
        Assert.Equal("Mehrere", session.UndoLabel);
        session.Undo();
        Assert.Same(original, session.Presentation);
    }
}

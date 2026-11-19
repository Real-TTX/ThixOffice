using ThixShow.Core.Documents;
using ThixShow.Core.Editing;
using ThixShow.Core.Model;
using ThixShow.Core.Tests.Formats;

namespace ThixShow.Core.Tests.Editing;

public class RandomEditTests
{
    /// <summary>One random command on the session (anything the ribbon or keyboard can do).</summary>
    private static void RandomCommand(EditingSession session, Random random)
    {
        var slide = session.CurrentSlide!;
        var shapes = slide.Shapes.Select(shape => shape.Id).ToList();
        int AnyShape() => shapes[random.Next(shapes.Count)];
        var size = session.Presentation.SlideSize;
        EmuRect AnyBox() => new(random.Next(0, (int)(size.Width / 2)), random.Next(0, (int)(size.Height / 2)), random.Next(100000, 3000000), random.Next(100000, 2000000));

        switch (random.Next(30))
        {
            case 0:
                session.AddSlide();
                break;
            case 1:
                session.DuplicateSlides([slide.Id]);
                break;
            case 2:
                session.DeleteSlides([slide.Id]);
                break;
            case 3:
                session.MoveSlides([slide.Id], random.Next(session.Presentation.SlideCount));
                break;
            case 4:
                session.SetSlidesHidden([slide.Id], random.Next(2) == 0);
                break;
            case 5:
                session.ChangeLayout([slide.Id], session.Presentation.Masters[0].Layouts[random.Next(session.Presentation.Masters[0].Layouts.Count)].Id);
                break;
            case 6:
                session.InsertShape(new[] { "rect", "ellipse", "roundRect", "rightArrow", "star5" }[random.Next(5)], AnyBox());
                break;
            case 7:
                session.InsertTextBox(AnyBox(), "Neu");
                break;
            case 8 when shapes.Count > 0:
                session.SelectShapes([AnyShape()]);
                session.MoveShapes(random.Next(-500000, 500000), random.Next(-500000, 500000), nudge: random.Next(2) == 0);
                break;
            case 9 when shapes.Count > 0:
                session.SelectShapes([AnyShape()]);
                session.Rotate(random.Next(-180, 180), relative: random.Next(2) == 0);
                break;
            case 10 when shapes.Count > 0:
                session.SelectShapes([AnyShape()]);
                session.Flip(random.Next(2) == 0);
                break;
            case 11 when shapes.Count > 1:
                session.SelectShapes([AnyShape(), AnyShape()]);
                session.GroupShapes();
                break;
            case 12 when shapes.Count > 0:
                session.SelectShapes([AnyShape()]);
                session.Ungroup();
                break;
            case 13 when shapes.Count > 0:
                session.SelectShapes([AnyShape()]);
                session.Arrange((ZOrderMove)random.Next(4));
                break;
            case 14 when shapes.Count > 0:
                session.SelectShapes([AnyShape()]);
                session.SetFill(new SolidFill(ColorSpec.Scheme("accent" + random.Next(1, 7))));
                break;
            case 15 when shapes.Count > 0:
                session.SelectShapes([AnyShape()]);
                session.DeleteShapes();
                break;
            case 16 when shapes.Count > 0:
                session.SelectShapes([AnyShape()]);
                session.DuplicateShapes();
                break;
            case 17:
            case 18:
            case 19:
                {
                    var textShape = slide.Shapes.OfType<AutoShape>().FirstOrDefault(shape => shape.Text is not null);
                    if (textShape is null)
                    {
                        break;
                    }

                    var target = new TextTarget(textShape.Id);
                    var body = session.TextBodyOf(target)!;
                    var paragraph = random.Next(body.Paragraphs.Count);
                    var offset = random.Next(body.Paragraphs[paragraph].Length + 1);
                    session.SetCaret(target, new TextPosition(paragraph, offset), extend: false);
                    switch (random.Next(8))
                    {
                        case 0:
                            session.InsertText("abc");
                            break;
                        case 1:
                            session.InsertParagraphBreak();
                            break;
                        case 2:
                            session.DeleteBackward();
                            break;
                        case 3:
                            session.DeleteForward(word: true);
                            break;
                        case 4:
                            session.SelectAllText();
                            session.ToggleBold();
                            break;
                        case 5:
                            session.HandleTab(random.Next(2) == 0);
                            break;
                        case 6:
                            session.ToggleNumbering();
                            break;
                        default:
                            session.MoveCaret(CaretMovement.WordRight, extend: true);
                            session.SetFontSize(random.Next(10, 60));
                            break;
                    }

                    break;
                }

            case 20:
                session.SetTransition([slide.Id], new SlideTransition((TransitionKind)random.Next(1, 8)));
                break;
            case 21 when shapes.Count > 0:
                session.SelectShapes([AnyShape()]);
                session.AddAnimation((AnimationPreset)random.Next(20), (AnimationDirection)random.Next(4), (AnimationTrigger)random.Next(3));
                break;
            case 22 when slide.Timing is { MainSequence.Count: > 1 }:
                session.MoveAnimation(0, 1);
                break;
            case 23:
                session.SetNotesText(slide.Id, "Notiz " + random.Next(100));
                break;
            case 24:
                session.InsertTable(random.Next(1, 4), random.Next(1, 4));
                break;
            case 25:
                session.AddSection("Abschnitt " + random.Next(100), slide.Id);
                break;
            case 26 when shapes.Count > 0:
                session.SelectShapes([AnyShape()]);
                var fragment = session.Copy();
                if (fragment is not null)
                {
                    session.Paste(fragment);
                }

                break;
            case 27:
                session.ReplaceAll(new FindOptions("Punkt"), "Satz");
                break;
            case 28:
                session.GoToSlide(session.Presentation.Slides[random.Next(session.Presentation.SlideCount)].Id);
                break;
            default:
                if (random.Next(10) == 0)
                {
                    session.ApplyDesign(BuiltInThemes.All[random.Next(BuiltInThemes.All.Count)]);
                }

                break;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Undoing_everything_gives_back_the_original_and_redoing_gives_the_result(int seed)
    {
        var random = new Random(seed);
        var original = TestDecks.Deck(4);
        var time = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);
        var session = new EditingSession(original, () => time);
        for (var step = 0; step < 150; step++)
        {
            time = time.AddMilliseconds(random.Next(0, 4000));
            RandomCommand(session, random);
            Assert.All(session.Presentation.Slides, slide => Assert.NotNull(session.Presentation.FindLayout(slide.LayoutId)));
            Assert.Equal(session.Presentation.Slides.Count, session.Presentation.Slides.Select(slide => slide.Id).Distinct().Count());
            foreach (var slide in session.Presentation.Slides)
            {
                var ids = ShapeTree.All(slide.Shapes).Select(shape => shape.Id).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
            }
        }

        var final = session.Presentation;
        while (session.CanUndo)
        {
            session.Undo();
        }

        Assert.Same(original, session.Presentation);
        while (session.CanRedo)
        {
            session.Redo();
        }

        Assert.Same(final, session.Presentation);

        // What the session made is a valid file that reads back the same.
        var read = PptxTestSupport.RoundTrip(final);
        PptxTestSupport.AssertSameModel(final, read);
    }

    [Fact]
    public void Documents_read_and_write_pptx_and_pptm()
    {
        var document = new ShowDocument("Präsentation1");
        Assert.Single(document.Presentation.Slides);
        Assert.False(document.IsDirty);
        document.Session.AddSlide();
        Assert.True(document.IsDirty);

        var macro = PptxTestSupport.Write(TestDecks.Deck(1) with { IsMacroEnabled = true });
        using var stream = new MemoryStream(macro);
        var reader = typeof(ThixOffice.Core.Documents.OfficeDocument).GetMethod("ReadContent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        reader.Invoke(document, [stream]);
        Assert.Equal(ShowFileFormat.Pptm, document.LoadedFormat);
        Assert.True(document.HasMacros);

        var writer = typeof(ThixOffice.Core.Documents.OfficeDocument).GetMethod("WriteContentAs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        using var pptx = new MemoryStream();
        writer.Invoke(document, [pptx, ThixShowApp.Definition.DocumentTypes.First(type => type.Extension == ".pptx")]);
        using var package = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(new MemoryStream(pptx.ToArray()), false);
        Assert.Equal(DocumentFormat.OpenXml.PresentationDocumentType.Presentation, package.DocumentType);
        PptxTestSupport.AssertValid(pptx.ToArray());
    }
}

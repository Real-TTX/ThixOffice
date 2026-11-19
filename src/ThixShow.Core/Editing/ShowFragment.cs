using ThixShow.Core.Formats.Pptx;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

/// <summary>What a clipboard fragment holds.</summary>
public enum FragmentKind
{
    Slides,
    Shapes,
    Text,
}

/// <summary>
/// ThixShow's clipboard content: slides, shapes or formatted text, held as a small presentation (its masters and one slide per
/// copied slide, or one slide with the copied shapes / a text box with the copied paragraphs). <see cref="ToBytes"/> writes it as a
/// pptx for the system clipboard (the app's own format), <see cref="FromBytes"/> reads it back; <see cref="PlainText"/> is the text
/// for other programs.
/// </summary>
public sealed record ShowFragment(FragmentKind Kind, Presentation Content)
{
    private const string Marker = "ThixShow.Fragment.";

    /// <summary>The fragment's text (paragraphs and shapes' texts separated by line breaks).</summary>
    public string PlainText => string.Join("\n", Content.Slides.SelectMany(slide => ShapeTree.All(slide.Shapes))
        .Select(shape => shape switch
        {
            AutoShape { Text: { } text } => text.PlainText,
            GraphicFrame { Table: { } table } => string.Join("\n", table.Rows.Select(row => string.Join("\t", row.Cells.Select(cell => cell.Text.PlainText)))),
            _ => null,
        })
        .Where(text => !string.IsNullOrEmpty(text)));

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        PptxWriter.Write(Content with { Title = Marker + Kind }, stream);
        return stream.ToArray();
    }

    /// <summary>Reads a fragment written by <see cref="ToBytes"/> (null for anything else).</summary>
    public static ShowFragment? FromBytes(byte[] bytes)
    {
        try
        {
            var content = PptxReader.Read(new MemoryStream(bytes));
            if (content.Title is not { } title || !title.StartsWith(Marker, StringComparison.Ordinal) || !Enum.TryParse<FragmentKind>(title[Marker.Length..], out var kind))
            {
                return null;
            }

            return new ShowFragment(kind, content with { Title = null });
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or FormatException or DocumentFormat.OpenXml.Packaging.OpenXmlPackageException or System.Xml.XmlException)
        {
            return null;
        }
    }
}

public sealed partial class EditingSession
{
    /// <summary>The selection as a fragment (text, shapes or slides), or null when nothing is selected.</summary>
    public ShowFragment? Copy()
    {
        if (CurrentSlide is not { } slide)
        {
            return null;
        }

        var shell = Presentation with { Slides = default, Sections = default, OriginalSlides = null, UnsupportedContent = default };
        if (Selection.Text is { IsCollapsed: false } text && TextBodyOf(text.Target) is { } body)
        {
            var box = new AutoShape
            {
                Id = 2,
                Name = "Textfeld 1",
                IsTextBox = true,
                Transform = new Transform2D(0, 0, Presentation.SlideSize.Width / 2, Presentation.SlideSize.Height / 6),
                Geometry = ShapeGeometry.Rectangle,
                Fill = NoFill.Instance,
                Text = new TextBody(new BodyProperties(Wrap: true, AutoFit: ShapeAutoFit.Instance), ListStyle.Empty, TextEdits.Slice(body, text.Start, text.End).ToList()),
            };
            return new ShowFragment(FragmentKind.Text, shell with { Slides = [new Slide { Id = 256, LayoutId = slide.LayoutId, Shapes = [box] }] });
        }

        var ids = SelectedShapeIds();
        if (ids.Count > 0)
        {
            var context = SlideContext.For(Presentation, slide);
            var shapes = ids.Select(slide.FindShape).OfType<SlideElement>()
                .Select(shape => shape.Transform is null ? shape with { Transform = ShapeResolver.Resolve(context, shape).Transform } : shape)
                .ToList();
            return new ShowFragment(FragmentKind.Shapes, shell with { Slides = [new Slide { Id = 256, LayoutId = slide.LayoutId, Shapes = shapes }] });
        }

        var slides = Selection.Slides.Select(Presentation.FindSlide).OfType<Slide>().OrderBy(item => Presentation.IndexOf(item.Id)).ToList();
        return slides.Count == 0 ? null : new ShowFragment(FragmentKind.Slides, shell with { Slides = slides });
    }

    /// <summary>Copies and deletes the selection (text, shapes or slides).</summary>
    public ShowFragment? Cut()
    {
        var fragment = Copy();
        if (fragment is null)
        {
            return null;
        }

        switch (fragment.Kind)
        {
            case FragmentKind.Text:
                DeleteBackward();
                break;
            case FragmentKind.Shapes:
                DeleteShapes();
                break;
            default:
                DeleteSlides(fragment.Content.Slides.Select(slide => slide.Id).ToList());
                break;
        }

        return fragment;
    }

    /// <summary>
    /// Pastes a fragment: text into the text being edited (else as a new text box), shapes onto the current slide (offset when they would
    /// cover their originals), slides after the current slide with the destination's layouts ("Zieldesign verwenden").
    /// </summary>
    public void Paste(ShowFragment fragment)
    {
        if (CurrentSlide is not { } slide)
        {
            return;
        }

        switch (fragment.Kind)
        {
            case FragmentKind.Text when Selection.Text is { } selection:
                {
                    var paragraphs = fragment.Content.Slides.SelectMany(item => item.Shapes).OfType<AutoShape>().SelectMany(shape => shape.Text?.Paragraphs ?? []).ToList();
                    Apply("Einfügen", presentation => WithText(presentation, slide.Id, selection.Target, body =>
                            TextEdits.InsertParagraphs(TextEdits.Delete(body, selection.Start, selection.End), selection.Start, paragraphs).Body),
                        after =>
                        {
                            var body = TextBodyOf(after, slide.Id, selection.Target)!;
                            var end = paragraphs.Count == 1
                                ? new TextPosition(selection.Start.Paragraph, selection.Start.Offset + paragraphs[0].Length)
                                : new TextPosition(selection.Start.Paragraph + paragraphs.Count - 1, paragraphs[^1].Length);
                            return Selection with { Text = TextSelection.Caret(selection.Target, TextEdits.Clamp(body, end)) };
                        });
                    return;
                }

            case FragmentKind.Text or FragmentKind.Shapes:
                PasteShapes(slide, fragment.Content.Slides.SelectMany(item => item.Shapes).ToList());
                return;
            default:
                PasteSlides(fragment.Content);
                return;
        }
    }

    /// <summary>Pastes plain text: into the text being edited, else as a new text box.</summary>
    public void PasteText(string text)
    {
        if (Selection.Text is not null)
        {
            InsertText(text.Replace("\r\n", "\n"));
            return;
        }

        var size = Presentation.SlideSize;
        InsertTextBox(new EmuRect(size.Width / 4, size.Height / 3, size.Width / 2, size.Height / 8), text.Replace("\r\n", "\n"));
    }

    private void PasteShapes(Slide slide, List<SlideElement> shapes)
    {
        var pasted = new List<int>();
        Apply("Einfügen", presentation => WithSlide(presentation, slide.Id, current =>
        {
            var next = NextShapeId(current);
            var existing = current.Shapes.Select(shape => shape.Transform).Where(transform => transform is not null).ToHashSet();
            var offset = shapes.Any(shape => existing.Contains(shape.Transform)) ? DuplicateOffset : 0;
            var added = new List<SlideElement>();
            foreach (var shape in shapes)
            {
                var copy = Renumber(Offset(shape with { Placeholder = null }, offset, offset), ref next);
                pasted.Add(copy.Id);
                added.Add(copy);
            }

            return current with { Shapes = current.Shapes.AddRange(added) };
        }), _ => Selection with { Shapes = pasted, Text = null });
    }

    private void PasteSlides(Presentation content)
    {
        var current = CurrentSlide!;
        var ids = new List<uint>();
        Apply("Einfügen", presentation =>
        {
            var next = presentation.NextSlideId;
            var index = presentation.IndexOf(current.Id) + 1;
            var slides = presentation.Slides.ToList();
            var sections = presentation.Sections;
            var after = current.Id;
            foreach (var source in content.Slides)
            {
                var type = content.FindLayout(source.LayoutId)?.Type ?? "obj";
                var master = presentation.MasterOf(current);
                var layout = master.Layouts.FirstOrDefault(item => item.Type == type) ?? master.Layouts.FirstOrDefault(item => item.Type == "obj") ?? master.Layouts[0];
                var copy = Copy(source, next) with { LayoutId = layout.Id };
                slides.Insert(index++, copy);
                sections = AddToSection(sections, after, next);
                after = next;
                ids.Add(next++);
            }

            return presentation with { Slides = slides, Sections = sections };
        }, _ => new ShowSelection(ids.LastOrDefault(), ids));
    }
}

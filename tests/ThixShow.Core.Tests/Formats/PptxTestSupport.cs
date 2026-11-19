using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ThixShow.Core.Formats.Pptx;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Formats;

/// <summary>Writing, validating, reading and comparing pptx files in the tests.</summary>
internal static class PptxTestSupport
{
    public static byte[] Write(Presentation presentation)
    {
        using var stream = new MemoryStream();
        PptxWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    public static Presentation Read(byte[] bytes) => PptxReader.Read(new MemoryStream(bytes));

    /// <summary>Asserts that the Open XML validator finds no error.</summary>
    public static void AssertValid(byte[] bytes)
    {
        using var package = PresentationDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(20).Select(error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}")));
    }

    public static Presentation RoundTrip(Presentation presentation)
    {
        var bytes = Write(presentation);
        AssertValid(bytes);
        return Read(bytes);
    }

    /// <summary>The model without what only the file layer knows (kept XML, part names, relationship ids, the package).</summary>
    public static Presentation Strip(Presentation presentation) => presentation with
    {
        Source = null,
        Package = null,
        OriginalSlides = null,
        UnsupportedContent = default,
        TableStyles = default,
        DefaultTableStyleId = null,
        NotesMasterPartUri = null,
        Masters = presentation.Masters.Select(master => master with
        {
            Source = null,
            PartUri = null,
            References = default,
            ThemePartUri = null,
            Theme = master.Theme with { Source = null },
            Shapes = StripShapes(master.Shapes),
            Layouts = master.Layouts.Select(layout => layout with { Source = null, PartUri = null, References = default, Shapes = StripShapes(layout.Shapes) }).ToList(),
        }).ToList(),
        Slides = presentation.Slides.Select(slide => slide with
        {
            Source = null,
            PartUri = null,
            References = default,
            Shapes = StripShapes(slide.Shapes),
            Transition = slide.Transition is null ? null : slide.Transition with { Source = null },
            Timing = slide.Timing is null ? null : slide.Timing with
            {
                Source = null,
                MainSequence = slide.Timing.MainSequence.Select(effect => effect with { Extra = null }).ToList(),
            },
            Notes = slide.Notes is null ? null : slide.Notes with { Source = null, PartUri = null, References = default, Shapes = StripShapes(slide.Notes.Shapes) },
        }).ToList(),
    };

    public static ValueList<SlideElement> StripShapes(ValueList<SlideElement> shapes) =>
        shapes.Select(shape => shape switch
        {
            GroupShape group => group with { Source = null, References = default, Children = StripShapes(group.Children) },
            _ => shape with { Source = null, References = default },
        }).ToList();

    /// <summary>Asserts equality slide by slide and shape by shape, so a failure names what differs.</summary>
    public static void AssertSameModel(Presentation expected, Presentation actual)
    {
        var (left, right) = (Strip(expected), Strip(actual));
        Assert.Equal(left.SlideSize, right.SlideSize);
        Assert.Equal(left.DefaultTextStyle, right.DefaultTextStyle);
        Assert.Equal(left.Masters.Count, right.Masters.Count);
        for (var index = 0; index < left.Masters.Count; index++)
        {
            var (first, second) = (left.Masters[index], right.Masters[index]);
            Assert.Equal(first.Theme.Colors, second.Theme.Colors);
            Assert.Equal(first.Theme.Fonts, second.Theme.Fonts);
            Assert.Equal(first.Theme.Formats, second.Theme.Formats);
            Assert.Equal(first.TextStyles, second.TextStyles);
            Assert.Equal(first.Shapes.ToList(), second.Shapes.ToList());
            for (var layout = 0; layout < first.Layouts.Count; layout++)
            {
                Assert.Equal(first.Layouts[layout].Shapes.ToList(), second.Layouts[layout].Shapes.ToList());
                Assert.Equal(first.Layouts[layout], second.Layouts[layout]);
            }

            Assert.Equal(first, second);
        }

        Assert.Equal(left.Slides.Count, right.Slides.Count);
        for (var index = 0; index < left.Slides.Count; index++)
        {
            var (first, second) = (left.Slides[index], right.Slides[index]);
            for (var shape = 0; shape < first.Shapes.Count; shape++)
            {
                Assert.Equal(first.Shapes[shape], second.Shapes[shape]);
            }

            Assert.Equal(first.Transition, second.Transition);
            Assert.Equal(first.Timing?.MainSequence.ToList(), second.Timing?.MainSequence.ToList());
            Assert.Equal(first.Notes, second.Notes);
            Assert.Equal(first, second);
        }

        Assert.Equal(left.Sections, right.Sections);
        Assert.Equal(left, right);
    }
}

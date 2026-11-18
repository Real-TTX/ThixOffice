using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Tests.Formats;

/// <summary>Writing, validating and comparing docx files in the tests.</summary>
internal static class DocxTestSupport
{
    public static byte[] Write(Document document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    public static Document Read(byte[] bytes) => DocxReader.Read(new MemoryStream(bytes));

    /// <summary>Asserts that the Open XML validator finds no error in <paramref name="bytes"/>.</summary>
    public static void AssertValid(byte[] bytes)
    {
        using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}")));
    }

    /// <summary>Writes, validates and reads back.</summary>
    public static Document RoundTrip(Document document)
    {
        var bytes = Write(document);
        AssertValid(bytes);
        return Read(bytes);
    }

    /// <summary>Asserts content equality with a message that names the first difference.</summary>
    public static void AssertSameContent(Document expected, Document actual)
    {
        Assert.Equal(expected.Blocks.Count, actual.Blocks.Count);
        for (var index = 0; index < expected.Blocks.Count; index++)
        {
            var first = expected.Blocks[index];
            var second = actual.Blocks[index];
            if (first is Paragraph left && second is Paragraph right)
            {
                Assert.Equal(left.Properties, right.Properties);
                Assert.Equal(left.MarkProperties, right.MarkProperties);
                Assert.Equal(left.SectionBreak, right.SectionBreak);
                Assert.Equal(left.Inlines.ToList(), right.Inlines.ToList());
            }

            Assert.True(first.ContentEquals(second), $"Block {index} differs: {first} / {second}");
        }

        Assert.Equal(expected.Styles.CharacterDefaults, actual.Styles.CharacterDefaults);
        Assert.Equal(expected.Styles.ParagraphDefaults, actual.Styles.ParagraphDefaults);
        Assert.Equal(expected.Styles.Styles.ToList(), actual.Styles.Styles.ToList());
        Assert.Equal(expected.Styles.LatentStylesXml, actual.Styles.LatentStylesXml);
        Assert.Equal(expected.Numbering.Definitions.ToList(), actual.Numbering.Definitions.ToList());
        Assert.Equal(expected.Numbering.Instances.ToList(), actual.Numbering.Instances.ToList());
        Assert.Equal(expected.FinalSection, actual.FinalSection);
        Assert.Equal(expected.Settings, actual.Settings);
        Assert.True(expected.ContentEquals(actual));
    }

    /// <summary>The XML of document.xml in <paramref name="bytes"/>.</summary>
    public static string DocumentXml(byte[] bytes)
    {
        using var package = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        return package.MainDocumentPart!.Document!.OuterXml;
    }

    public static OpenXmlAttribute WordAttribute(string name, string value) =>
        new("w", name, "http://schemas.openxmlformats.org/wordprocessingml/2006/main", value);
}

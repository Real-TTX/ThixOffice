using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using Xunit;

namespace ThixOffice.Core.Tests;

/// <summary>The rules of printing: which pages a choice means, the paper names the systems get.</summary>
public sealed class PrintServiceTests
{
    [Theory]
    [InlineData("1-3, 5", new[] { 0, 1, 2, 4 })]
    [InlineData("2;4", new[] { 1, 3 })]
    [InlineData(" 5 – 3 ", new[] { 4, 3, 2 })]
    [InlineData("7", new[] { 6 })]
    public void Typed_pages_are_numbers_and_ranges(string text, int[] expected) =>
        Assert.Equal(expected, PrintService.PagesOf(PrintRange.Custom, text, 0, 7));

    [Theory]
    [InlineData("", "Bitte geben Sie die Seiten an")]
    [InlineData("8", "nur 7 Seiten")]
    [InlineData("0", "keine Seitenangabe")]
    [InlineData("a-b", "keine Seitenangabe")]
    [InlineData("1-2-3", "keine Seitenangabe")]
    public void Wrong_pages_are_refused_at_the_field(string text, string message)
    {
        var exception = Assert.Throws<BusinessException>(() => PrintService.PagesOf(PrintRange.Custom, text, 0, 7));
        Assert.Contains(message, exception.Message);
        Assert.Equal(PrintService.PagesField, exception.Field);
    }

    [Fact]
    public void All_and_the_current_page()
    {
        Assert.Equal([0, 1, 2], PrintService.PagesOf(PrintRange.All, "9", 1, 3));
        Assert.Equal([2], PrintService.PagesOf(PrintRange.Current, null, 5, 3));
    }

    [Fact]
    public void Paper_is_found_in_either_orientation_and_named_for_CUPS()
    {
        Assert.Equal("a4", PaperSize.Find(841.9, 595.3)?.Id);
        Assert.Null(PaperSize.Find(500, 700));
        Assert.Equal("Letter", PaperSize.CupsMedia(612, 792));
        Assert.Equal("Custom.500x700", PaperSize.CupsMedia(700, 500));
        Assert.Equal("Benutzerdefiniert (17,64 × 24,69 cm)", PaperSize.Describe(500, 700));
    }
}

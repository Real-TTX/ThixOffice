using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using Xunit;

namespace ThixOffice.Core.Tests;

/// <summary>Which addresses a link may have and open.</summary>
public sealed class LinkServiceTests
{
    [Theory]
    [InlineData("https://www.thix.de/seite?x=1", "https://www.thix.de/seite?x=1")]
    [InlineData("www.thix.de", "https://www.thix.de")]
    [InlineData("anna@example.de", "mailto:anna@example.de")]
    [InlineData(" mailto:anna@example.de ", "mailto:anna@example.de")]
    public void Web_and_mail_addresses_are_links(string address, string expected) => Assert.Equal(expected, LinkService.Normalize(address));

    [Theory]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"C:\Programme\x.exe")]
    [InlineData("ftp://server/datei")]
    public void Everything_else_is_refused_and_never_opened(string address)
    {
        var opened = new List<string>();
        var exception = Assert.Throws<BusinessException>(() => LinkService.Open(address, opened.Add));
        Assert.Equal(LinkService.AddressField, exception.Field);
        Assert.Empty(opened);
    }
}

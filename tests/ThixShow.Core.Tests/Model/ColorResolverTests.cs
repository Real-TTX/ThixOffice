using ThixShow.Core.Drawing;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Model;

public class ColorResolverTests
{
    // The colours of a classic Office-like scheme (only used as known inputs: accent1 #4472C4).
    private static readonly ColorScheme Scheme = ColorScheme.FromHex("Test",
        "000000", "FFFFFF", "44546A", "E7E6E6", "4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47", "0563C1", "954F72");

    private static readonly ColorContext Context = new(Scheme, ColorMap.Default);

    private static string Resolve(ColorSpec color, ColorContext? context = null) => ColorResolver.Resolve(color, context ?? Context).ToHex();

    [Theory]
    [InlineData(75000, null, "2F5597")] // "Darker 25%"
    [InlineData(50000, null, "203864")] // "Darker 50%"
    [InlineData(60000, 40000, "8FAADC")] // "Lighter 40%"
    [InlineData(40000, 60000, "B4C7E7")] // "Lighter 60%"
    [InlineData(20000, 80000, "DAE3F3")] // "Lighter 80%"
    public void Luminance_transforms_give_the_documented_theme_shades(int lumMod, int? lumOff, string expected)
    {
        var color = ColorSpec.Scheme("accent1").LumMod(lumMod);
        if (lumOff is { } offset)
        {
            color = color.LumOff(offset);
        }

        Assert.Equal(expected, Resolve(color));
    }

    [Fact]
    public void Shade_works_in_linear_light()
    {
        Assert.Equal("2F528F", Resolve(ColorSpec.Scheme("accent1").Shade(50000)));
    }

    [Fact]
    public void Tint_blends_with_white_in_linear_light()
    {
        Assert.Equal("FFFFFF", Resolve(ColorSpec.Rgb("4472C4").Tint(0)));
        Assert.Equal("4472C4", Resolve(ColorSpec.Rgb("4472C4").Tint(100000)));
        var half = ColorResolver.Resolve(ColorSpec.Rgb("000000").Tint(50000), Context);
        Assert.Equal(188, half.R); // sRGB of linear 0.5
    }

    [Fact]
    public void Scheme_names_go_through_the_colour_map()
    {
        Assert.Equal("FFFFFF", Resolve(ColorSpec.Scheme("bg1")));
        Assert.Equal("000000", Resolve(ColorSpec.Scheme("tx1")));
        var dark = new ColorContext(Scheme, new ColorMap("dk1", "lt1", "dk2", "lt2"));
        Assert.Equal("000000", Resolve(ColorSpec.Scheme("bg1"), dark));
        Assert.Equal("FFFFFF", Resolve(ColorSpec.Scheme("tx1"), dark));
        Assert.Equal("44546A", Resolve(ColorSpec.Scheme("dk2"), dark));
    }

    [Fact]
    public void Alpha_transforms_set_the_opacity()
    {
        var color = ColorResolver.Resolve(ColorSpec.Rgb("FF0000").Alpha(50000), Context);
        Assert.Equal(128, color.A);
        var modulated = ColorResolver.Resolve(ColorSpec.Rgb("FF0000").Alpha(50000).With("alphaMod", 50000), Context);
        Assert.Equal(64, modulated.A);
        Assert.Equal(0.5, ColorSpec.Rgb("FF0000").Alpha(50000).Opacity, 3);
        Assert.Equal(1, ColorSpec.Rgb("FF0000").Alpha(50000).WithOpacity(1).Opacity);
    }

    [Fact]
    public void Placeholder_colour_takes_the_referencing_colour_and_both_transform_lists()
    {
        var context = Context.WithPlaceholder(ColorSpec.Scheme("accent1"));
        Assert.Equal("4472C4", Resolve(ColorSpec.Placeholder, context));
        Assert.Equal("2F528F", Resolve(ColorSpec.Placeholder.Shade(50000), context));
    }

    [Fact]
    public void Other_bases_resolve()
    {
        Assert.Equal("6495ED", Resolve(ColorSpec.Preset("cornflowerBlue")));
        Assert.Equal("006400", Resolve(ColorSpec.Preset("dkGreen")));
        Assert.Equal("123456", Resolve(ColorSpec.System("windowText", "123456")));
        Assert.Equal("FF0000", Resolve(new ColorSpec(ColorKind.Hsl, "0,100000,50000")));
        Assert.Equal("FFFFFF", Resolve(new ColorSpec(ColorKind.ScRgb, "100000,100000,100000")));
    }

    [Fact]
    public void Hue_and_saturation_transforms()
    {
        Assert.Equal("00FFFF", Resolve(ColorSpec.Rgb("FF0000").With("comp")));
        Assert.Equal("00FFFF", Resolve(ColorSpec.Rgb("FF0000").With("inv")));
        Assert.Equal("808080", Resolve(ColorSpec.Rgb("FF0000").With("sat", 0)));
        Assert.Equal("00FF00", Resolve(ColorSpec.Rgb("FF0000").With("hueOff", 7200000)));
    }
}

using Avalonia.Media;
using SkiaSharp;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>
/// The colours that are document data, not chrome (the one file the colour rules test exempts): paper, PowerPoint's standard
/// colours, the colour palettes made of the presentation's theme, the slide show's black and white screens and the laser dot.
/// Everything around the slides comes from Mat tokens.
/// </summary>
public static class ShowPalette
{
    /// <summary>The paper of printed pages and PDF handouts.</summary>
    public static readonly SKColor PaperSkia = new(0xFF, 0xFF, 0xFF);

    /// <summary>A slide that is not drawn yet (white, like an empty slide).</summary>
    public static readonly SKColor EmptySlide = new(0xFF, 0xFF, 0xFF);

    /// <summary>The slide show's "B" screen and the area around a slide of another aspect ratio.</summary>
    public static readonly SKColor ShowBlack = new(0x00, 0x00, 0x00);

    /// <summary>The slide show's "W" screen.</summary>
    public static readonly SKColor ShowWhite = new(0xFF, 0xFF, 0xFF);

    /// <summary>The background of the presenter view (PowerPoint's is dark in every theme).</summary>
    public static readonly Color PresenterBackground = Color.FromRgb(0x1F, 0x1F, 0x1F);

    /// <summary>The text of the presenter view.</summary>
    public static readonly Color PresenterText = Color.FromRgb(0xF2, 0xF2, 0xF2);

    /// <summary>The muted text of the presenter view.</summary>
    public static readonly Color PresenterMuted = Color.FromRgb(0xA6, 0xA6, 0xA6);

    /// <summary>The default of Schriftfarbe's button (PowerPoint: red), Fülleffekt's (accent 1 is the theme's, so: orange) and Formkontur's.</summary>
    public const string DefaultFontColor = "#FF0000";

    public const string DefaultFillColor = "#FFC000";

    public const string DefaultOutlineColor = "#000000";

    /// <summary>PowerPoint's ten standard colours.</summary>
    public static IReadOnlyList<string> StandardColors { get; } =
    [
        "#C00000", "#FF0000", "#FFC000", "#FFFF00", "#92D050", "#00B050", "#00B0F0", "#0070C0", "#002060", "#7030A0",
    ];

    /// <summary>The scheme slots of a palette row in PowerPoint's order: Hintergrund 1, Text 1, Hintergrund 2, Text 2, Akzent 1–6.</summary>
    private static readonly string[] Slots = ["bg1", "tx1", "bg2", "tx2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6"];

    /// <summary>The colour palette of the ribbon's colour pickers: the theme row, four rows of tints/shades, the standard colours.</summary>
    public static IReadOnlyList<string> Colors(Presentation presentation)
    {
        var master = presentation.Masters.Count > 0 ? presentation.Masters[0] : null;
        if (master is null)
        {
            return StandardColors;
        }

        var context = new ColorContext(master.Theme.Colors, master.ColorMap);
        var colors = new List<string>();
        colors.AddRange(Slots.Select(slot => Hex(ColorResolver.Resolve(ColorSpec.Scheme(slot), context))));
        foreach (var (tint, shade) in new[] { (80000, 0), (60000, 0), (40000, 0), (0, 75000) })
        {
            foreach (var slot in Slots)
            {
                var spec = ColorSpec.Scheme(slot);
                spec = shade > 0 ? spec.LumMod(shade) : spec.LumMod(100000 - tint).LumOff(tint);
                colors.Add(Hex(ColorResolver.Resolve(spec, context)));
            }
        }

        colors.AddRange(StandardColors);
        return colors;
    }

    /// <summary>The sample deck's picture (a PNG made here): a night sky with northern lights above mountains and a lake.</summary>
    public static byte[] SamplePicture(int width = 960, int height = 640)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var sky = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, height * 0.7f),
                    [new SKColor(0x0B, 0x16, 0x2E), new SKColor(0x1B, 0x3A, 0x5C), new SKColor(0x2E, 0x6B, 0x7A)], SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, width, height, sky);
            using var aurora = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = height * 0.09f, StrokeCap = SKStrokeCap.Round, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, height * 0.035f) };
            foreach (var (color, offset) in new[] { (new SKColor(0x5C, 0xF2, 0xA8, 0xB0), 0f), (new SKColor(0x3E, 0xC9, 0xD8, 0x80), height * 0.07f) })
            {
                using var path = new SKPath();
                path.MoveTo(-20, (height * 0.30f) + offset);
                path.CubicTo(width * 0.30f, (height * 0.12f) + offset, width * 0.55f, (height * 0.42f) + offset, width + 20, (height * 0.18f) + offset);
                aurora.Color = color;
                canvas.DrawPath(path, aurora);
            }

            using var stars = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0xC0), IsAntialias = true };
            var random = new Random(7);
            for (var index = 0; index < 90; index++)
            {
                canvas.DrawCircle((float)(random.NextDouble() * width), (float)(random.NextDouble() * height * 0.45), (float)(0.6 + random.NextDouble() * 1.2), stars);
            }

            foreach (var (color, peaks, baseline) in new[] { (new SKColor(0x22, 0x33, 0x44), new[] { 0.15f, 0.42f, 0.28f, 0.55f, 0.35f }, 0.62f), (new SKColor(0x14, 0x20, 0x2C), new[] { 0.48f, 0.30f, 0.52f, 0.38f, 0.58f }, 0.72f) })
            {
                using var mountains = new SKPath();
                mountains.MoveTo(0, height);
                for (var index = 0; index < peaks.Length; index++)
                {
                    mountains.LineTo(width * index / (float)(peaks.Length - 1), height * (baseline - (peaks[index] * 0.25f)));
                }

                mountains.LineTo(width, height);
                mountains.Close();
                using var paint = new SKPaint { Color = color, IsAntialias = true };
                canvas.DrawPath(mountains, paint);
            }

            using var lake = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, height * 0.82f), new SKPoint(0, height), [new SKColor(0x1F, 0x4E, 0x5F), new SKColor(0x0B, 0x16, 0x2E)], SKShaderTileMode.Clamp) };
            canvas.DrawRect(0, height * 0.82f, width, height * 0.18f, lake);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>"#RRGGBB" as a document colour.</summary>
    public static ColorSpec? ToColorSpec(string? hex) =>
        hex is { Length: >= 7 } ? ColorSpec.Rgb(hex.TrimStart('#')[..6].ToUpperInvariant()) : null;

    public static string Hex(RgbColor color) => "#" + color.ToHex();

    /// <summary>A document colour as an Avalonia colour (the swatches of menus).</summary>
    public static Color ToColor(RgbColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>A "#rrggbb" as a brush (swatches of menus and galleries).</summary>
    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}

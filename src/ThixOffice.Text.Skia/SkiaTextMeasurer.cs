using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using HarfBuzzSharp;
using SkiaSharp;
using ThixOffice.Text.Unicode;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFont = HarfBuzzSharp.Font;

namespace ThixOffice.Text.Skia;

/// <summary>
/// Measures text with the system's fonts: SkiaSharp finds the typefaces and their metrics, HarfBuzz shapes (kerning, ligatures,
/// combining marks), characters the requested font lacks are shaped with a fallback typeface (SKFontManager.MatchCharacter).
/// The advances are grapheme-cluster safe as <see cref="ShapedText"/> documents. Thread-safe (one lock around every call);
/// typefaces and HarfBuzz fonts are cached for the lifetime of the measurer.
/// </summary>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly object _lock = new();
    private readonly SKFontManager _fontManager = SKFontManager.Default;
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> _typefaces = [];
    private readonly Dictionary<FontFace, SKTypeface> _faces = [];
    private readonly Dictionary<SKTypeface, ShapingFont> _shapingFonts = [];
    private readonly Dictionary<FontSpec, FontMetrics> _metrics = [];
    private readonly Dictionary<(SKTypeface Typeface, int CodePoint), SKTypeface?> _fallbacks = [];
    private IReadOnlyList<string>? _families;
    private bool _disposed;

    public FontMetrics GetMetrics(FontSpec font)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_metrics.TryGetValue(font, out var cached))
            {
                return cached;
            }

            var typeface = ResolveTypeface(font.Family, font.Bold, font.Italic);
            using var skFont = new SKFont(typeface, (float)font.Size);
            var metrics = skFont.Metrics;
            var size = font.Size;
            var ascent = metrics.Ascent < 0 ? -metrics.Ascent : size * 0.8;
            var descent = metrics.Descent > 0 ? metrics.Descent : size * 0.2;
            var result = new FontMetrics(
                ascent,
                descent,
                Math.Max(0, metrics.Leading),
                PositiveOr(metrics.UnderlinePosition, size * 0.1),
                PositiveOr(metrics.UnderlineThickness, size * 0.05),
                metrics.StrikeoutPosition is { } strike && strike < 0 ? -strike : size * 0.3,
                PositiveOr(metrics.StrikeoutThickness, size * 0.05));
            _metrics[font] = result;
            return result;
        }
    }

    public ShapedText Shape(string text, FontSpec font)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var advances = new double[text.Length];
            if (text.Length == 0)
            {
                return new ShapedText(text, font, advances, []);
            }

            var primary = ResolveTypeface(font.Family, font.Bold, font.Italic);
            var segments = SplitByTypeface(text, primary);
            var runs = new List<(SKTypeface Typeface, int Start, int Length, List<(ushort Glyph, int Cluster, double Pen, double Offset)> Glyphs)>();
            foreach (var (typeface, start, length) in segments)
            {
                var glyphs = ShapeSegment(text, start, length, typeface, font.Size, advances);
                runs.Add((typeface, start, length, glyphs));
            }

            // x of the start of every code unit (prefix sums of the grapheme-safe advances).
            var prefix = new double[text.Length + 1];
            for (var index = 0; index < text.Length; index++)
            {
                prefix[index + 1] = prefix[index] + advances[index];
            }

            var glyphRuns = new List<ShapedGlyphRun>(runs.Count);
            foreach (var (typeface, start, length, glyphs) in runs)
            {
                glyphRuns.Add(BuildGlyphRun(text, typeface, start, length, glyphs, prefix, font));
            }

            return new ShapedText(text, font, advances, glyphRuns);
        }
    }

    public IReadOnlyList<string> GetFamilies()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _families ??= _fontManager.FontFamilies
                .Where(family => !string.IsNullOrWhiteSpace(family))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(family => family, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    /// <summary>The typeface a glyph run of this measurer was shaped with (renderers draw its glyph ids with exactly it).</summary>
    public SKTypeface GetTypeface(FontFace face)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _faces.TryGetValue(face, out var typeface) ? typeface : ResolveTypeface(face.Family, face.Bold, face.Italic);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var shapingFont in _shapingFonts.Values)
            {
                shapingFont.Dispose();
            }

            // The typefaces are not disposed: SkiaSharp hands out one managed wrapper per native typeface, shared with every
            // other user in the process (other measurers, Avalonia's own text rendering); they live as long as the font manager.
            _shapingFonts.Clear();
            _typefaces.Clear();
            _faces.Clear();
            _fallbacks.Clear();
        }
    }

    private static double PositiveOr(float? value, double fallback) => value is { } number && number > 0 ? number : fallback;

    private SKTypeface ResolveTypeface(string family, bool bold, bool italic)
    {
        var key = (family, bold, italic);
        if (_typefaces.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var typeface = _fontManager.MatchFamily(family, StyleOf(bold, italic))
                       ?? _fontManager.MatchFamily(null, StyleOf(bold, italic))
                       ?? SKTypeface.Default;
        _typefaces[key] = typeface;
        Register(typeface, bold, italic);
        return typeface;
    }

    private static SKFontStyle StyleOf(bool bold, bool italic) => new(
        bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
        SKFontStyleWidth.Normal,
        italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

    private FontFace Register(SKTypeface typeface, bool bold, bool italic)
    {
        var face = new FontFace(typeface.FamilyName, bold, italic);
        _faces.TryAdd(face, typeface);
        return face;
    }

    /// <summary>Splits the text into runs of graphemes that one typeface covers (the primary one where it can).</summary>
    private List<(SKTypeface Typeface, int Start, int Length)> SplitByTypeface(string text, SKTypeface primary)
    {
        var segments = new List<(SKTypeface Typeface, int Start, int Length)>();
        var index = 0;
        while (index < text.Length)
        {
            var length = TextBoundaries.GraphemeLength(text, index);
            var typeface = TypefaceFor(text, index, length, primary);
            if (segments.Count > 0 && ReferenceEquals(segments[^1].Typeface, typeface))
            {
                var last = segments[^1];
                segments[^1] = (last.Typeface, last.Start, last.Length + length);
            }
            else
            {
                segments.Add((typeface, index, length));
            }

            index += length;
        }

        return segments;
    }

    private SKTypeface TypefaceFor(string text, int start, int length, SKTypeface primary)
    {
        var rune = Rune.GetRuneAt(text, start);
        if (Rune.IsControl(rune) || primary.ContainsGlyph(rune.Value))
        {
            return primary;
        }

        var key = (primary, rune.Value);
        if (!_fallbacks.TryGetValue(key, out var fallback))
        {
            var style = primary.FontStyle;
            fallback = _fontManager.MatchCharacter(primary.FamilyName, style, null, rune.Value);
            if (fallback is not null)
            {
                var bold = style.Weight >= (int)SKFontStyleWeight.SemiBold;
                var italic = style.Slant != SKFontStyleSlant.Upright;
                fallback = Deduplicate(fallback, bold, italic);
            }

            _fallbacks[key] = fallback;
        }

        return fallback ?? primary;
    }

    /// <summary>Returns the cached instance of an equal typeface (same family and style) so that segments join.</summary>
    private SKTypeface Deduplicate(SKTypeface typeface, bool bold, bool italic)
    {
        var face = new FontFace(typeface.FamilyName, bold, italic);
        if (_faces.TryGetValue(face, out var existing))
        {
            return existing;
        }

        _faces[face] = typeface;
        return typeface;
    }

    private List<(ushort Glyph, int Cluster, double Pen, double Offset)> ShapeSegment(
        string text, int start, int length, SKTypeface typeface, double size, double[] advances)
    {
        var shapingFont = ShapingFontOf(typeface);
        var scale = size / shapingFont.UnitsPerEm;
        using var buffer = new HbBuffer();
        buffer.AddUtf16(text, start, length);
        buffer.GuessSegmentProperties();
        shapingFont.Font.Shape(buffer);

        var infos = buffer.GlyphInfos;
        var positions = buffer.GlyphPositions;
        var clusterAdvance = new Dictionary<int, double>();
        var glyphs = new List<(ushort Glyph, int Cluster, double Pen, double Offset)>(infos.Length);
        var pen = 0.0;
        for (var index = 0; index < infos.Length; index++)
        {
            var cluster = (int)infos[index].Cluster;
            var advance = positions[index].XAdvance * scale;
            clusterAdvance[cluster] = clusterAdvance.GetValueOrDefault(cluster) + advance;
            glyphs.Add(((ushort)infos[index].Codepoint, cluster, pen, positions[index].XOffset * scale));
            pen += advance;
        }

        DistributeAdvances(text, start, length, clusterAdvance, advances);
        return glyphs;
    }

    /// <summary>Gives each cluster's advance to the grapheme starts inside it (evenly); controls get nothing.</summary>
    private static void DistributeAdvances(string text, int start, int length, Dictionary<int, double> clusterAdvance, double[] advances)
    {
        var clusters = clusterAdvance.Keys.Order().ToList();
        var graphemeStarts = new List<int>();
        for (var index = 0; index < clusters.Count; index++)
        {
            var clusterStart = clusters[index];
            var clusterEnd = index + 1 < clusters.Count ? clusters[index + 1] : start + length;
            graphemeStarts.Clear();
            var position = clusterStart;
            while (position < clusterEnd)
            {
                graphemeStarts.Add(position);
                position += TextBoundaries.GraphemeLength(text, position);
            }

            var share = clusterAdvance[clusterStart] / Math.Max(1, graphemeStarts.Count);
            foreach (var graphemeStart in graphemeStarts)
            {
                if (graphemeStart < start + length)
                {
                    advances[graphemeStart] = IsControl(text, graphemeStart) ? 0 : share;
                }
            }
        }
    }

    private static bool IsControl(string text, int index) =>
        char.IsControl(text[index]) || CharUnicodeInfo.GetUnicodeCategory(text[index]) == UnicodeCategory.Format;

    private ShapedGlyphRun BuildGlyphRun(
        string text,
        SKTypeface typeface,
        int start,
        int length,
        List<(ushort Glyph, int Cluster, double Pen, double Offset)> glyphs,
        double[] prefix,
        FontSpec font)
    {
        var face = _faces.FirstOrDefault(pair => ReferenceEquals(pair.Value, typeface)).Key;
        if (face.Family is null)
        {
            face = Register(typeface, font.Bold, font.Italic);
        }

        // Pen at the first glyph of each cluster, so that glyphs sit at their cluster's x (which drops control advances).
        var clusterPen = new Dictionary<int, double>();
        foreach (var glyph in glyphs)
        {
            clusterPen.TryAdd(glyph.Cluster, glyph.Pen);
        }

        var ids = new List<ushort>(glyphs.Count);
        var xs = new List<float>(glyphs.Count);
        var clusters = new List<int>(glyphs.Count);
        foreach (var glyph in glyphs)
        {
            if (IsControl(text, glyph.Cluster))
            {
                continue;
            }

            ids.Add(glyph.Glyph);
            xs.Add((float)(prefix[glyph.Cluster] + glyph.Pen - clusterPen[glyph.Cluster] + glyph.Offset));
            clusters.Add(glyph.Cluster);
        }

        return new ShapedGlyphRun(face, start, length, [.. ids], [.. xs], [.. clusters]);
    }

    private ShapingFont ShapingFontOf(SKTypeface typeface)
    {
        if (_shapingFonts.TryGetValue(typeface, out var cached))
        {
            return cached;
        }

        var created = ShapingFont.Create(typeface);
        _shapingFonts[typeface] = created;
        return created;
    }

    /// <summary>A HarfBuzz font for a typeface, scaled in font units.</summary>
    private sealed class ShapingFont : IDisposable
    {
        private readonly Blob _blob;
        private readonly Face _face;

        private ShapingFont(Blob blob, Face face, HbFont font, int unitsPerEm)
        {
            _blob = blob;
            _face = face;
            Font = font;
            UnitsPerEm = unitsPerEm;
        }

        public HbFont Font { get; }

        public int UnitsPerEm { get; }

        public static ShapingFont Create(SKTypeface typeface)
        {
            using var stream = typeface.OpenStream(out var collectionIndex);
            var bytes = ReadAll(stream);

            // HarfBuzz keeps reading the font data: it lives in unmanaged memory that the blob frees (Blob.FromStream would
            // hand over a pointer into a managed array that the garbage collector may move).
            var data = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
            Marshal.Copy(bytes, 0, data, bytes.Length);
            var blob = new Blob(data, bytes.Length, MemoryMode.ReadOnly, () => Marshal.FreeHGlobal(data));
            blob.MakeImmutable();
            var face = new Face(blob, collectionIndex);
            var unitsPerEm = face.UnitsPerEm > 0 ? face.UnitsPerEm : 2048;
            face.UnitsPerEm = unitsPerEm;
            var font = new HbFont(face);
            font.SetScale(unitsPerEm, unitsPerEm);
            return new ShapingFont(blob, face, font, unitsPerEm);
        }

        public void Dispose()
        {
            Font.Dispose();
            _face.Dispose();
            _blob.Dispose();
        }

        private static byte[] ReadAll(SKStreamAsset? stream)
        {
            if (stream is null)
            {
                return [];
            }

            var length = stream.Length;
            var bytes = new byte[length];
            var read = stream.Read(bytes, length);
            return read == length ? bytes : bytes[..read];
        }
    }
}

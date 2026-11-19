namespace ThixOffice.Text;

/// <summary>
/// A measurer that remembers what it shaped: a grid shows the same short texts frame after frame (scrolling, typing
/// elsewhere), and shaping them again each time would cost more than drawing them. Holds up to <c>capacity</c> texts and
/// starts over when full (a simple bound, no LRU bookkeeping on the hot path). Not thread-safe – one per UI thread.
/// </summary>
public sealed class CachingTextMeasurer(ITextMeasurer inner, int capacity = 20_000) : ITextMeasurer
{
    private readonly Dictionary<(string Text, FontSpec Font), ShapedText> _shaped = [];

    public ITextMeasurer Inner => inner;

    public FontMetrics GetMetrics(FontSpec font) => inner.GetMetrics(font);

    public ShapedText Shape(string text, FontSpec font)
    {
        if (_shaped.TryGetValue((text, font), out var shaped))
        {
            return shaped;
        }

        if (_shaped.Count >= capacity)
        {
            _shaped.Clear();
        }

        shaped = inner.Shape(text, font);
        _shaped[(text, font)] = shaped;
        return shaped;
    }

    public IReadOnlyList<string> GetFamilies() => inner.GetFamilies();
}

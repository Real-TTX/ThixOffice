namespace ThixWrite.Core.Model;

/// <summary>What changed in a document.</summary>
public enum DocumentChangeKind
{
    /// <summary>Blocks [<see cref="DocumentChange.StartBlock"/>, + RemovedCount) were replaced by InsertedCount new ones.</summary>
    Blocks,
    /// <summary>The style sheet (every paragraph may look different).</summary>
    Styles,
    /// <summary>The list definitions (labels may change).</summary>
    Numbering,
    /// <summary>The final section's page setup or the document settings.</summary>
    Layout,
    /// <summary>Everything (a document was loaded into the same object).</summary>
    All,
}

/// <summary>One change of a document, as the layout and the views see it.</summary>
public sealed record DocumentChange(DocumentChangeKind Kind, int StartBlock = 0, int RemovedCount = 0, int InsertedCount = 0)
{
    public static readonly DocumentChange Everything = new(DocumentChangeKind.All);
}

/// <summary>A section: blocks [<see cref="StartBlock"/>, <see cref="EndBlock"/>] with one page setup.</summary>
public sealed record SectionInfo(int Index, int StartBlock, int EndBlock, SectionProperties Properties);

/// <summary>
/// A word-processing document: the body as a list of immutable blocks (paragraphs and preserved blocks; a section ends at a
/// paragraph with <see cref="Paragraph.SectionBreak"/>, the last section's setup is <see cref="FinalSection"/>), the styles, the
/// list definitions, the theme, settings, the notes about preserved content and the original package.
/// <para>
/// Invariants: there is at least one block and the last block is a paragraph outside tables (so the caret always has a place
/// after a table, as in Word); a table's paragraphs are in reading order with every cell present (<see cref="TableSpan"/>). The model is
/// changed only through the editing engine (internal mutators), which records every change for undo and raises
/// <see cref="Changed"/> – the layout listens to it to re-lay only what changed.
/// </para>
/// </summary>
public sealed class Document
{
    private readonly List<Block> _blocks;
    private SectionInfo[]? _sections;

    public Document(
        IEnumerable<Block> blocks,
        StyleSheet? styles = null,
        NumberingDefinitions? numbering = null,
        SectionProperties? finalSection = null,
        Theme? theme = null,
        DocumentSettings? settings = null)
    {
        _blocks = [.. blocks];
        if (_blocks.Count == 0 || _blocks[^1] is not Paragraph { Cell: null })
        {
            _blocks.Add(Paragraph.Empty());
        }

        Styles = styles ?? BuiltInStyles.CreateDefault();
        Numbering = numbering ?? NumberingDefinitions.Empty;
        FinalSection = finalSection ?? SectionProperties.Default;
        Theme = theme ?? Theme.Office;
        Settings = settings ?? DocumentSettings.Default;
    }

    /// <summary>"Seitenfarbe" (w:background w:color) as "RRGGBB", or null for white paper. Kept in the package's document element.</summary>
    public string? PageColor { get; internal set; }

    /// <summary>Raised after every change (the editing engine's steps, undo and redo).</summary>
    public event EventHandler<DocumentChange>? Changed;

    /// <summary>Identifies this document object (the clipboard knows whether a fragment comes from the same document).</summary>
    public Guid Id { get; } = Guid.NewGuid();

    public IReadOnlyList<Block> Blocks => _blocks;

    public StyleSheet Styles { get; private set; }

    public NumberingDefinitions Numbering { get; private set; }

    /// <summary>The page setup of the last section (w:body/w:sectPr).</summary>
    public SectionProperties FinalSection { get; private set; }

    public Theme Theme { get; private set; }

    public DocumentSettings Settings { get; private set; }

    /// <summary>What the file contained that ThixWrite keeps without fully supporting it (empty for new documents).</summary>
    public IReadOnlyList<UnsupportedContent> UnsupportedContent { get; init; } = [];

    /// <summary>The original docx package (null for new and plain-text documents).</summary>
    public PreservedPackage? Package { get; init; }

    /// <summary>Counts every change (cheap "did anything change since" checks for views and caches).</summary>
    public long Version { get; private set; }

    /// <summary>An empty document with the built-in styles, A4.</summary>
    public static Document CreateBlank() => new([Paragraph.Empty()]);

    /// <summary>A document with one paragraph per line of <paramref name="text"/> in the default style.</summary>
    public static Document FromPlainText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return new Document(lines.Select(line => new Paragraph(line)));
    }

    public int BlockCount => _blocks.Count;

    /// <summary>The paragraph at block <paramref name="index"/>, or null when that block is not a paragraph.</summary>
    public Paragraph? ParagraphAt(int index) => index >= 0 && index < _blocks.Count ? _blocks[index] as Paragraph : null;

    /// <summary>The last position of the document (before the last paragraph mark).</summary>
    public DocumentPosition End => new(_blocks.Count - 1, _blocks[^1].Length);

    /// <summary>The sections, in order.</summary>
    public IReadOnlyList<SectionInfo> Sections => _sections ??= BuildSections();

    /// <summary>The section that block <paramref name="index"/> belongs to.</summary>
    public SectionInfo SectionOf(int index)
    {
        var sections = Sections;
        var low = 0;
        var high = sections.Count - 1;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (sections[middle].EndBlock < index)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return sections[low];
    }

    /// <summary>
    /// The header (or footer) a page shows: page <paramref name="pageInSection"/> (0-based) of section <paramref name="section"/>,
    /// page number <paramref name="pageNumber"/> (1-based). The first page of a section with "Erste Seite anders" shows First, even
    /// pages show Even when the document says so, the others Default; a kind a section does not set comes from the sections before
    /// it. Also the kind, for the editor.
    /// </summary>
    public (Story? Story, HeaderFooterKind Kind) HeaderFooterOf(int section, int pageInSection, int pageNumber, bool footer)
    {
        var sections = Sections;
        var properties = sections[Math.Clamp(section, 0, sections.Count - 1)].Properties;
        var kind = properties.TitlePage && pageInSection == 0 ? HeaderFooterKind.First
            : Settings.EvenAndOddHeaders && pageNumber % 2 == 0 ? HeaderFooterKind.Even
            : HeaderFooterKind.Default;
        for (var index = Math.Clamp(section, 0, sections.Count - 1); index >= 0; index--)
        {
            var set = footer ? sections[index].Properties.Footers : sections[index].Properties.Headers;
            if (set[kind] is { } story)
            {
                return (story, kind);
            }
        }

        return (null, kind);
    }

    /// <summary>The whole text, paragraphs separated by "\n" (preserved blocks give their lines).</summary>
    public string GetPlainText() =>
        string.Join("\n", _blocks.Select(block => block switch
        {
            Paragraph paragraph => paragraph.Text,
            OpaqueBlock opaque => string.Join("\n", opaque.Lines),
            _ => "",
        }));

    /// <summary>True when both documents have the same content, styles, lists and page setup.</summary>
    public bool ContentEquals(Document other) =>
        _blocks.Count == other._blocks.Count
        && _blocks.Zip(other._blocks).All(pair => pair.First.ContentEquals(pair.Second))
        && Styles.ContentEquals(other.Styles)
        && Numbering.ContentEquals(other.Numbering)
        && FinalSection == other.FinalSection
        && Settings == other.Settings;

    /// <summary>Replaces blocks [<paramref name="start"/>, +<paramref name="count"/>) – for the editing engine only.</summary>
    internal void ReplaceBlocks(int start, int count, IReadOnlyList<Block> inserted)
    {
        _blocks.RemoveRange(start, count);
        _blocks.InsertRange(start, inserted);
        if (_blocks.Count == 0 || _blocks[^1] is not Paragraph { Cell: null })
        {
            throw new InvalidOperationException("The last block of a document must be a paragraph outside tables.");
        }

        _sections = null;
        Raise(new DocumentChange(DocumentChangeKind.Blocks, start, count, inserted.Count));
    }

    internal void SetStyles(StyleSheet styles)
    {
        Styles = styles;
        Raise(new DocumentChange(DocumentChangeKind.Styles));
    }

    internal void SetNumbering(NumberingDefinitions numbering)
    {
        Numbering = numbering;
        Raise(new DocumentChange(DocumentChangeKind.Numbering));
    }

    internal void SetFinalSection(SectionProperties section)
    {
        FinalSection = section;
        _sections = null;
        Raise(new DocumentChange(DocumentChangeKind.Layout));
    }

    internal void SetPageColor(string? color)
    {
        PageColor = color;
        Raise(new DocumentChange(DocumentChangeKind.Layout));
    }

    internal void SetSettings(DocumentSettings settings)
    {
        Settings = settings;
        Raise(new DocumentChange(DocumentChangeKind.Layout));
    }

    private void Raise(DocumentChange change)
    {
        Version++;
        Changed?.Invoke(this, change);
    }

    private SectionInfo[] BuildSections()
    {
        var sections = new List<SectionInfo>();
        var start = 0;
        for (var index = 0; index < _blocks.Count; index++)
        {
            if (_blocks[index] is Paragraph { SectionBreak: { } properties } && index < _blocks.Count - 1)
            {
                sections.Add(new SectionInfo(sections.Count, start, index, properties));
                start = index + 1;
            }
        }

        sections.Add(new SectionInfo(sections.Count, start, _blocks.Count - 1, FinalSection));
        return [.. sections];
    }
}

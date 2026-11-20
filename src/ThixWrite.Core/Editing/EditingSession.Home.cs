using System.Globalization;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>What "Sortieren" compares.</summary>
public enum SortKind
{
    Text,
    Number,
    Date,
}

/// <summary>
/// The rest of Start: Sortieren (paragraphs, table rows), Rahmen und Schattierung (paragraph borders and shading, page borders), the
/// multi-level lists, and Word's keys for Kapitälchen, doppelt unterstrichen, Abstand vor, hängender Einzug, Absatzformat zurücksetzen.
/// </summary>
public sealed partial class EditingSession
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    // ---------------------------------------------------------------- sorting

    /// <summary>
    /// "Sortieren": the selected paragraphs (outside tables) by their text – or, with the caret in a table, its rows by
    /// <paramref name="column"/> (grid column; heading rows stay on top). False when there is nothing to sort or a merged cell is in the way.
    /// </summary>
    public bool Sort(SortKind kind, bool descending, int column = 0)
    {
        if (CurrentTable is { } span)
        {
            return SortRows(span, kind, descending, column);
        }

        var (first, last) = SelectedBlocks();
        if (last <= first || Enumerable.Range(first, last - first + 1).Any(block => Document.Blocks[block] is not Paragraph { Cell: null }))
        {
            return false;
        }

        var paragraphs = Enumerable.Range(first, last - first + 1).Select(block => (Paragraph)Document.Blocks[block]).ToList();
        var sorted = Order(paragraphs, paragraph => paragraph.Text, kind, descending).Select(paragraph => (Block)paragraph.WithSectionBreak(null)).ToList();
        if (paragraphs[^1].SectionBreak is { } section)
        {
            sorted[^1] = ((Paragraph)sorted[^1]).WithSectionBreak(section); // the section break stays at the end
        }

        Run("Sortieren", EditKind.Other, null, () =>
        {
            Apply(new BlocksStep(first, [.. paragraphs], sorted, new BlockReplaceMap(first, paragraphs.Count, sorted)));
            SetSelectionQuiet(new Selection(new DocumentPosition(first, 0), new DocumentPosition(last, ((Paragraph)Document.Blocks[last]).Length)));
        });
        return true;
    }

    private bool SortRows(TableSpan span, SortKind kind, bool descending, int column)
    {
        var table = span.Table;
        if (table.Rows.Any(row => row.Cells.Any(cell => cell.VerticalMerge != VerticalMerge.None)))
        {
            return false;
        }

        var model = TableEdits.TableModel.Load(Document, span);
        var header = Math.Max(table.HeaderRowCount, 0);
        var body = model.Rows.Skip(header).ToList();
        string KeyOf(TableEdits.RowModel row)
        {
            var start = row.Properties.GridBefore;
            foreach (var cell in row.Cells)
            {
                if (column >= start && column < start + cell.Span)
                {
                    return string.Join(" ", cell.Paragraphs.Select(paragraph => paragraph.Text));
                }

                start += cell.Span;
            }

            return "";
        }

        var ordered = Order(body, KeyOf, kind, descending).ToList();
        model.Rows.RemoveRange(header, body.Count);
        model.Rows.AddRange(ordered);
        var blocks = model.Emit();
        Run("Sortieren", EditKind.Other, null, () =>
        {
            Apply(TableEdits.Replace(Document, span, blocks));
            SetSelectionQuiet(Selection.Caret(new DocumentPosition(span.Start, 0)));
        });
        return true;
    }

    /// <summary>A stable order by text (German collation), number (the first number in the text) or date (dd.MM.yyyy).</summary>
    private static IEnumerable<T> Order<T>(IEnumerable<T> items, Func<T, string> key, SortKind kind, bool descending)
    {
        IComparer<T> comparer = kind switch
        {
            SortKind.Number => Comparer<T>.Create((left, right) => Nullable.Compare(NumberOf(key(left)), NumberOf(key(right)))),
            SortKind.Date => Comparer<T>.Create((left, right) => Nullable.Compare(DateOf(key(left)), DateOf(key(right)))),
            _ => Comparer<T>.Create((left, right) => German.CompareInfo.Compare(key(left).Trim(), key(right).Trim(), CompareOptions.IgnoreCase)),
        };
        return descending ? items.OrderByDescending(item => item, comparer) : items.OrderBy(item => item, comparer);
    }

    private static double? NumberOf(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"-?\d+(?:[.,]\d+)*");
        return match.Success && double.TryParse(match.Value.Replace(".", "", StringComparison.Ordinal).Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTime? DateOf(string text) =>
        DateTime.TryParse(text.Trim(), German, DateTimeStyles.None, out var date) ? date : null;

    // ---------------------------------------------------------------- borders and shading

    /// <summary>"Rahmen" of the selected paragraphs (null removes them); one step.</summary>
    public void SetParagraphBorders(BorderSet? borders) =>
        FormatParagraphs("Rahmen", properties => properties with { Borders = borders ?? new BorderSet(BorderLine.None, BorderLine.None, BorderLine.None, BorderLine.None, BorderLine.None) });

    /// <summary>"Schattierung" of the selected paragraphs ("RRGGBB", null: keine Farbe).</summary>
    public void SetParagraphShading(string? fill) =>
        FormatParagraphs("Schattierung", properties => properties with { Shading = fill is null ? null : new Shading(fill.TrimStart('#').ToUpperInvariant()) });

    /// <summary>"Seitenränder": a border around every page of the caret's section (null removes it), measured from the page edge.</summary>
    public void SetPageBorders(BorderSet? borders) =>
        SetPageSetup(section => section with
        {
            PageBorders = borders,
            UnknownXml = WithPageBorders(section.UnknownXml, borders),
        }, PageSetupScope.CurrentSection);

    private static string? WithPageBorders(string? xml, BorderSet? borders)
    {
        var rest = xml is null ? "" : System.Text.RegularExpressions.Regex.Replace(xml, @"<w:pgBorders\b[^>]*?(/>|>.*?</w:pgBorders>)", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (borders is not null)
        {
            string Side(string name, BorderLine? line) => line is null ? "" :
                $"<w:{name} w:val=\"{line.Style}\" w:sz=\"{(int)Math.Round(line.Width * 8)}\" w:space=\"{(int)Math.Round(line.Space)}\" w:color=\"{line.Color ?? "auto"}\" />";
            rest += $"<w:pgBorders w:offsetFrom=\"page\" xmlns:w=\"{WordNamespace}\">{Side("top", borders.Top)}{Side("left", borders.Left)}{Side("bottom", borders.Bottom)}{Side("right", borders.Right)}</w:pgBorders>";
        }

        return rest.Length == 0 ? null : rest;
    }

    // ---------------------------------------------------------------- lists

    /// <summary>"Mehrstufige Liste": the selected paragraphs in a new list of the gallery's style (<see cref="BuiltInLists.CreateMultilevel"/>), keeping their levels.</summary>
    public void ApplyMultilevelList(string style) =>
        Run("Mehrstufige Liste", EditKind.Other, null, () =>
        {
            var definition = BuiltInLists.CreateMultilevel(style, Document.Numbering.NextDefinitionId());
            var numbering = Document.Numbering.WithDefinition(definition);
            var instance = new NumberingInstance { Id = numbering.NextInstanceId(), AbstractId = definition.Id };
            Apply(DocumentSteps.Numbering(Document, numbering.WithInstance(instance)));
            EnsureStyle(BuiltInStyles.ListParagraph);
            FormatSelectedParagraphs(item =>
            {
                var level = Resolver.ResolveParagraph(item).Numbering?.Level ?? 0;
                var styleId = item.Properties.StyleId is null or BuiltInStyles.Normal ? BuiltInStyles.ListParagraph : item.Properties.StyleId;
                return item.WithProperties(item.Properties with { Numbering = new NumberingReference(instance.Id, level), StyleId = styleId });
            });
        });

    /// <summary>The list level (0–8) of the caret's paragraph, or null outside lists.</summary>
    public int? CurrentListLevel => Document.ParagraphAt(_selection.Start.Block) is { } paragraph ? Resolver.ResolveParagraph(paragraph).Numbering?.Level : null;

    // ---------------------------------------------------------------- Word's keys

    private EffectiveCharacterFormat FormatAtSelection()
    {
        var paragraph = Document.ParagraphAt(_selection.Start.Block) ?? Paragraph.Empty();
        var properties = paragraph.Length == 0 ? paragraph.MarkProperties : paragraph.InlineAt(Math.Min(_selection.Start.Offset, paragraph.Length - 1))?.Properties ?? paragraph.MarkProperties;
        return Resolver.ResolveCharacter(paragraph, properties);
    }

    /// <summary>Strg+Umschalt+Q: "Kapitälchen" on or off.</summary>
    public void ToggleSmallCaps()
    {
        var on = !FormatAtSelection().SmallCaps;
        FormatCharacters("Kapitälchen", (_, props) => props with { SmallCaps = on ? true : null });
    }

    /// <summary>Strg+Umschalt+D: doppelt unterstrichen on or off.</summary>
    public void ToggleDoubleUnderline() =>
        SetUnderline(FormatAtSelection().Underline == UnderlineStyle.Double ? UnderlineStyle.None : UnderlineStyle.Double);

    /// <summary>Strg+0: 12 pt space before the paragraph on or off.</summary>
    public void ToggleSpaceBefore()
    {
        var paragraph = Document.ParagraphAt(_selection.Start.Block) ?? Paragraph.Empty();
        var on = Resolver.ResolveParagraph(paragraph).SpacingBefore < 11.9;
        FormatParagraphs("Abstand vor", properties => properties with { SpacingBefore = on ? 12 : 0 });
    }

    /// <summary>Strg+T / Strg+Umschalt+T: the hanging indent one tab stop more (or less).</summary>
    public void ChangeHangingIndent(int direction)
    {
        var step = Math.Max(1, Document.Settings.DefaultTabStop);
        Run(direction > 0 ? "Hängender Einzug" : "Hängenden Einzug verkleinern", EditKind.Other, null, () =>
            FormatSelectedParagraphs(item =>
            {
                var format = Resolver.ResolveParagraph(item);
                var hanging = Math.Max(0, -format.FirstLineIndent);
                var indent = Math.Max(0, Math.Round((format.IndentLeft + direction * step) / step) * step);
                if (direction < 0 && format.IndentLeft <= 0)
                {
                    return item;
                }

                return item.WithProperties(item.Properties with { IndentLeft = indent, FirstLineIndent = -(hanging > 0 ? Math.Min(indent, hanging + direction * step) : Math.Min(indent, step)) });
            }));
    }

    /// <summary>Strg+Q: the paragraph formats come from the style again (list and section stay).</summary>
    public void ResetParagraphFormatting() =>
        FormatParagraphs("Absatzformat zurücksetzen", properties => new ParagraphProperties { StyleId = properties.StyleId, Numbering = properties.Numbering, UnknownXml = properties.UnknownXml });
}

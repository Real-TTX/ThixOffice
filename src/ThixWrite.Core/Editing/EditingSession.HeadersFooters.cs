using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>Headers, footers, page numbers and fields (Einfügen › Kopfzeile / Fußzeile / Seitenzahl, the tab Kopf- und Fußzeile).</summary>
public sealed partial class EditingSession
{
    /// <summary>The style of new header and footer paragraphs ("Kopfzeile" / "Fußzeile").</summary>
    public const string HeaderStyle = "Header";

    public const string FooterStyle = "Footer";

    /// <summary>The section the caret is in.</summary>
    public int CurrentSection => Document.SectionOf(_selection.Active.Block).Index;

    /// <summary>Sets the header (or footer) of a kind in a section (null removes it: the section before is shown again).</summary>
    public void SetHeaderFooter(int section, HeaderFooterKind kind, bool footer, Story? story) =>
        ChangeSection(section, footer ? "Fußzeile" : "Kopfzeile", properties => footer
            ? properties with { Footers = properties.Footers.With(kind, story) }
            : properties with { Headers = properties.Headers.With(kind, story) });

    /// <summary>"Erste Seite anders" for the section of the caret.</summary>
    public void SetTitlePage(bool value, int? section = null) =>
        ChangeSection(section ?? CurrentSection, "Erste Seite anders", properties => properties with { TitlePage = value });

    /// <summary>"Gerade & ungerade Seiten unterschiedlich" (the whole document).</summary>
    public void SetEvenAndOddHeaders(bool value)
    {
        if (Document.Settings.EvenAndOddHeaders == value)
        {
            return;
        }

        Run("Gerade & ungerade Seiten unterschiedlich", EditKind.Other, null, () =>
            Apply(DocumentSteps.Settings(Document, Document.Settings with { EvenAndOddHeaders = value })));
    }

    /// <summary>
    /// Einfügen › Seitenzahl: the default header (<paramref name="top"/>) or footer of the caret's section becomes a paragraph with the
    /// page number, aligned left, centred or right (Word's "Einfaches Seitenzahlformat"; what was there is replaced).
    /// </summary>
    public void InsertPageNumber(bool top, ParagraphAlignment alignment)
    {
        var style = top ? HeaderStyle : FooterStyle;
        Run("Seitenzahl", EditKind.Other, null, () =>
        {
            EnsureStyle(style);
            var paragraph = new Paragraph([FieldInline.Page(CharacterProperties.Empty)], new ParagraphProperties { StyleId = style, Alignment = alignment });
            var section = CurrentSection;
            var existing = top ? Document.Sections[section].Properties.Headers.Default : Document.Sections[section].Properties.Footers.Default;
            var story = new Story([paragraph], existing?.SourcePart);
            SetHeaderFooter(section, HeaderFooterKind.Default, !top, story);
        });
    }

    /// <summary>A field at the caret (Seitenzahl, Anzahl der Seiten, Datum) in the formatting of the text there.</summary>
    public void InsertField(FieldKind kind, DateTime? today = null)
    {
        var label = kind switch { FieldKind.Page => "Seitenzahl", FieldKind.NumPages => "Seitenanzahl", _ => "Datum" };
        Run(label, EditKind.Other, null, () =>
        {
            ClearWholeCells();
            var (properties, link) = TypingFormat();
            var field = kind switch
            {
                FieldKind.Page => FieldInline.Page(properties),
                FieldKind.NumPages => FieldInline.NumPages(properties),
                _ => FieldInline.Date(properties, today ?? DateTime.Today),
            } with { Link = link };
            var map = (ReplaceMap)Apply(ContentEdits.Replace(Document, _selection.Range, [new Paragraph([field])]));
            SetSelectionQuiet(Selection.Caret(map.EndOfInserted));
        });
    }

    /// <summary>An empty header or footer story (one paragraph in the style "Kopfzeile" / "Fußzeile", added to the styles when missing).</summary>
    public Story NewHeaderFooter(bool footer)
    {
        var style = footer ? FooterStyle : HeaderStyle;
        if (Document.Styles.Find(style) is null)
        {
            Run("Formatvorlage", EditKind.Other, null, () => EnsureStyle(style));
        }

        return new Story([Paragraph.Empty(new ParagraphProperties { StyleId = style })]);
    }

    private void ChangeSection(int section, string label, Func<SectionProperties, SectionProperties> change)
    {
        var sections = Document.Sections;
        if (section < 0 || section >= sections.Count)
        {
            return;
        }

        var info = sections[section];
        Run(label, EditKind.Other, null, () =>
        {
            if (section == sections.Count - 1)
            {
                var updated = change(Document.FinalSection);
                if (updated != Document.FinalSection)
                {
                    Apply(DocumentSteps.FinalSection(Document, updated));
                }

                return;
            }

            ApplyOptional(ContentEdits.FormatParagraphs(Document, info.EndBlock, info.EndBlock, paragraph =>
                paragraph.SectionBreak is { } properties ? paragraph.WithSectionBreak(change(properties)) : paragraph));
        });
    }
}

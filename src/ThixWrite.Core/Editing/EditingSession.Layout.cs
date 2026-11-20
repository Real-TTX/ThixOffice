using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>How "Groß-/Kleinschreibung" changes the selected text.</summary>
public enum CaseChange
{
    /// <summary>"Satzanfang groß".</summary>
    Sentence,

    /// <summary>"kleinbuchstaben".</summary>
    Lower,

    /// <summary>"GROSSBUCHSTABEN".</summary>
    Upper,

    /// <summary>"Ersten Buchstaben Im Wort Groß".</summary>
    Title,

    /// <summary>"gROSS-/kLEINSCHREIBUNG UMKEHREN".</summary>
    Toggle,
}

/// <summary>
/// Layout and review commands of the session: "Spalten", "Umbrüche" (Spalte, Abschnitt nächste Seite / fortlaufend / gerade /
/// ungerade), "Seitenfarbe", "Groß-/Kleinschreibung" and accepting or rejecting the tracked changes of a document – each one undoable step.
/// </summary>
public sealed partial class EditingSession
{

    /// <summary>"Spalten": the section of the caret in <paramref name="count"/> equal columns (a line between them when asked).</summary>
    public void SetColumns(int count, bool separator = false)
    {
        count = Math.Clamp(count, 1, 12);
        SetPageSetup(section => section with
        {
            ColumnCount = count,
            ColumnSeparator = separator && count > 1,
            Columns = EquatableArray<(double Width, double Space)>.Empty,
            UnknownXml = WithoutColumns(section.UnknownXml),
        }, PageSetupScope.CurrentSection);
    }

    /// <summary>"Spalten › Links/Rechts": two columns, the narrow one (a third of the width) left or right.</summary>
    public void SetUnequalColumns(bool narrowLeft)
    {
        SetPageSetup(section =>
        {
            var spacing = section.ColumnSpacing;
            var narrow = (section.ContentWidth - spacing) / 3;
            var wide = section.ContentWidth - spacing - narrow;
            return section with
            {
                ColumnCount = 2,
                ColumnSeparator = false,
                Columns = narrowLeft ? [(narrow, spacing), (wide, 0)] : [(wide, spacing), (narrow, 0)],
                UnknownXml = WithoutColumns(section.UnknownXml),
            };
        }, PageSetupScope.CurrentSection);
    }

    private static string? WithoutColumns(string? xml) =>
        xml is null ? null : Regex.Replace(xml, @"<w:cols\b[^>]*?(/>|>.*?</w:cols>)", "", RegexOptions.Singleline) is { Length: > 0 } rest ? rest : null;

    /// <summary>"Umbrüche › Spalte" (Strg+Umschalt+Eingabe): the text after the caret starts the next column.</summary>
    public void InsertColumnBreak()
    {
        if (CurrentTable is not null)
        {
            return;
        }

        Run("Spaltenumbruch", EditKind.Other, null, () =>
        {
            var (properties, _) = TypingFormat();
            var columnBreak = new OpaqueInline($"<w:br w:type=\"column\" xmlns:w=\"{WordNamespace}\" />", PreservedKind.ColumnBreak, true, properties);
            var map = (ReplaceMap)Apply(ContentEdits.Replace(Document, _selection.Range, [new Paragraph([columnBreak])]));
            SetSelectionQuiet(Selection.Caret(map.EndOfInserted));
        });
    }

    /// <summary>
    /// "Umbrüche › Abschnittsumbruch": the paragraph is split at the caret; the part before ends a section with the setup the caret's
    /// section has, the part after starts as <paramref name="start"/> says (nächste Seite, fortlaufend, gerade, ungerade Seite).
    /// </summary>
    public void InsertSectionBreak(SectionStart start)
    {
        if (CurrentTable is not null)
        {
            return;
        }

        var section = Document.SectionOf(_selection.Start.Block);
        var current = section.Properties;
        var isFinal = section.Index == Document.Sections.Count - 1;
        Run("Abschnittsumbruch", EditKind.Other, null, () =>
        {
            var split = (ReplaceMap)Apply(ContentEdits.SplitParagraph(Document, _selection.Range));
            var after = split.EndOfInserted;
            var before = after.Block - 1;
            // The part before ends the section it was in (its setup, its way of starting); the part after starts as chosen.
            ApplyOptional(ContentEdits.FormatParagraphs(Document, before, before, paragraph => paragraph.WithSectionBreak(current with { Headers = current.Headers, Footers = current.Footers })));
            if (isFinal)
            {
                Apply(DocumentSteps.FinalSection(Document, Document.FinalSection with { Start = start }));
            }
            else
            {
                var end = Document.SectionOf(after.Block).EndBlock;
                ApplyOptional(ContentEdits.FormatParagraphs(Document, end, end, paragraph =>
                    paragraph.SectionBreak is { } own ? paragraph.WithSectionBreak(own with { Start = start }) : paragraph));
            }

            SetSelectionQuiet(Selection.Caret(after));
        });
    }

    /// <summary>"Seitenfarbe": the colour of the pages ("RRGGBB"), null for no colour.</summary>
    public void SetPageColor(string? color)
    {
        var value = color is { Length: 6 } && !string.Equals(color, "FFFFFF", StringComparison.OrdinalIgnoreCase) ? color.ToUpperInvariant() : null;
        if (value == Document.PageColor)
        {
            return;
        }

        Run("Seitenfarbe", EditKind.Other, null, () =>
            Apply(new DocumentValueStep<PageColorValue>(new PageColorValue(Document.PageColor), new PageColorValue(value), (target, setting) => target.SetPageColor(setting.Color))));
    }

    /// <summary>A page colour as a reference value for <see cref="DocumentValueStep{T}"/>.</summary>
    private sealed record PageColorValue(string? Color);

    /// <summary>"Groß-/Kleinschreibung": the selected text (or the word at the caret) in another case; the formatting stays.</summary>
    public void ChangeCase(CaseChange change)
    {
        var range = _selection.Range;
        if (range.IsEmpty)
        {
            SelectWord(_selection.Start);
            range = _selection.Range;
            if (range.IsEmpty)
            {
                return;
            }
        }

        var culture = CultureInfo.GetCultureInfo("de-DE");
        var atStart = true;
        var inWord = false;
        string Transform(string text)
        {
            var characters = text.ToCharArray();
            for (var index = 0; index < characters.Length; index++)
            {
                var character = characters[index];
                var isLetter = char.IsLetter(character);
                characters[index] = change switch
                {
                    CaseChange.Lower => char.ToLower(character, culture),
                    CaseChange.Upper => char.ToUpper(character, culture),
                    CaseChange.Toggle => char.IsUpper(character) ? char.ToLower(character, culture) : char.ToUpper(character, culture),
                    CaseChange.Title => isLetter && !inWord ? char.ToUpper(character, culture) : char.ToLower(character, culture),
                    _ => isLetter && atStart ? char.ToUpper(character, culture) : char.ToLower(character, culture),
                };
                if (isLetter)
                {
                    atStart = false;
                }
                else if (character is '.' or '!' or '?')
                {
                    atStart = true;
                }

                inWord = isLetter || character is '\'' or '’' or '-' && inWord;
            }

            return new string(characters);
        }

        var selection = _selection;
        Run("Groß-/Kleinschreibung", EditKind.Other, null, () =>
        {
            ApplyOptional(ContentEdits.MapInlines(Document, range, inline => inline is TextRun run ? run with { Text = Transform(run.Text) } : inline));
            SetSelectionQuiet(selection);
        });
    }

    /// <summary>Umschalt+F3: lower case → UPPER CASE → Each Word Capitalised → lower case …, as the selection is now.</summary>
    public void CycleCase()
    {
        var text = _selection.IsCollapsed ? "" : string.Concat(Document.Blocks.OfType<Paragraph>().Skip(_selection.Start.Block).Take(_selection.End.Block - _selection.Start.Block + 1).Select(paragraph => paragraph.Text));
        var letters = text.Where(char.IsLetter).ToList();
        var change = letters.Count > 0 && letters.All(char.IsUpper) ? CaseChange.Title
            : letters.Count > 0 && letters.All(char.IsLower) ? CaseChange.Upper
            : CaseChange.Lower;
        ChangeCase(change);
    }

    /// <summary>True when the document holds tracked changes (insertions, deletions, moves, format changes) Word recorded.</summary>
    public bool HasRevisions => Document.Blocks.OfType<Paragraph>().Any(paragraph => paragraph.Inlines.Any(IsRevision) || HasFormatRevision(paragraph));

    /// <summary>"Alle Änderungen annehmen": insertions become ordinary text, deletions go, format changes keep the new format.</summary>
    public void AcceptAllRevisions() => ResolveRevisions(accept: true);

    /// <summary>"Alle Änderungen ablehnen": insertions go, deleted text comes back (format changes keep the format they have).</summary>
    public void RejectAllRevisions() => ResolveRevisions(accept: false);

    private void ResolveRevisions(bool accept)
    {
        if (!HasRevisions)
        {
            return;
        }

        Run(accept ? "Alle Änderungen annehmen" : "Alle Änderungen ablehnen", EditKind.Other, null, () =>
        {
            ApplyOptional(ContentEdits.FormatParagraphs(Document, 0, Document.Blocks.Count - 1, paragraph =>
            {
                if (!paragraph.Inlines.Any(IsRevision) && !HasFormatRevision(paragraph))
                {
                    return paragraph;
                }

                var inlines = new List<Inline>();
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline is not OpaqueInline { Kind: PreservedKind.TrackedChange } revision)
                    {
                        inlines.Add(inline is TextRun run ? run with { Properties = WithoutFormatRevision(run.Properties) } : inline);
                        continue;
                    }

                    var element = XElement.Parse(revision.Xml);
                    var inserted = element.Name.LocalName is "ins" or "moveTo";
                    var deleted = element.Name.LocalName is "del" or "moveFrom";
                    if (inserted == accept && (inserted || deleted))
                    {
                        var text = string.Concat(element.Descendants().Where(child => child.Name.LocalName is "t" or "delText").Select(child => child.Value));
                        if (text.Length > 0)
                        {
                            inlines.Add(new TextRun(text, WithoutFormatRevision(revision.Properties), revision.Link));
                        }
                    }

                    // The range markers of a move have nothing left to mark: they go with it.
                }

                return paragraph.WithInlines(inlines).WithMarkProperties(WithoutFormatRevision(paragraph.MarkProperties));
            }));
        });
    }

    private static bool IsRevision(Inline inline) => inline is OpaqueInline { Kind: PreservedKind.TrackedChange };

    private static bool HasFormatRevision(Paragraph paragraph) =>
        paragraph.Inlines.Any(inline => inline.Properties.UnknownXml?.Contains("rPrChange", StringComparison.Ordinal) == true)
        || paragraph.MarkProperties.UnknownXml is { } mark && (mark.Contains("<w:ins", StringComparison.Ordinal) || mark.Contains("<w:del", StringComparison.Ordinal));

    private static CharacterProperties WithoutFormatRevision(CharacterProperties properties)
    {
        if (properties.UnknownXml is not { } xml)
        {
            return properties;
        }

        var cleaned = Regex.Replace(xml, @"<w:(rPrChange|ins|del)\b[^>]*?(/>|>.*?</w:\1>)", "", RegexOptions.Singleline);
        return cleaned == xml ? properties : properties with { UnknownXml = cleaned.Length == 0 ? null : cleaned };
    }
}

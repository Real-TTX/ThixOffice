using System.Text;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>
/// The commands of Formatvorlagen: changing a style ("Formatvorlage ändern …"), making a new one ("Neue Formatvorlage …", "Auswahl als
/// neue Schnellformatvorlage"), the list the gallery and the pane show. Every change is one undoable step; the paragraphs that use a
/// style follow its change (the layout resolves the style again).
/// </summary>
public sealed partial class EditingSession
{
    /// <summary>
    /// The styles the gallery shows, in Word's order: the built-in quick styles (made when they are first used), then the document's own
    /// quick styles in the order of the file.
    /// </summary>
    public IReadOnlyList<Style> GalleryStyles()
    {
        var result = new List<Style>();
        foreach (var id in BuiltInStyles.GalleryOrder)
        {
            if ((Document.Styles.Find(id) ?? BuiltInStyles.Definition(id)) is { } style)
            {
                result.Add(style);
            }
        }

        result.AddRange(Document.Styles.Styles.Where(style => style.QuickFormat && style.Type is StyleType.Paragraph or StyleType.Character
                                                               && !result.Any(shown => shown.Id == style.Id) && !style.SemiHidden));
        return result;
    }

    /// <summary>The styles of the "Formatvorlagen" pane: every paragraph and character style the document has or Word offers, by name.</summary>
    public IReadOnlyList<Style> PaneStyles()
    {
        var result = Document.Styles.Styles.Where(style => style.Type is StyleType.Paragraph or StyleType.Character && !style.SemiHidden).ToList();
        foreach (var style in GalleryStyles())
        {
            if (!result.Any(shown => shown.Id == style.Id))
            {
                result.Add(style);
            }
        }

        return [.. result.OrderBy(style => BuiltInStyles.DisplayName(style), StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The paragraph style of the paragraph the selection starts in.</summary>
    public Style? CurrentParagraphStyle => Document.ParagraphAt(_selection.Start.Block) is { } paragraph ? Document.Styles.Find(Resolver.ParagraphStyleId(paragraph)) : null;

    /// <summary>Why <paramref name="name"/> cannot be the name of a new style (German, for the dialog), or null when it can.</summary>
    public string? StyleNameProblem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Bitte geben Sie einen Namen für die Formatvorlage ein.";
        }

        return Document.Styles.FindByName(name.Trim()) is not null || BuiltInStyles.IsBuiltInName(name.Trim())
            ? "Eine Formatvorlage mit diesem Namen gibt es schon. Bitte wählen Sie einen anderen."
            : null;
    }

    /// <summary>
    /// "Formatvorlage ändern": the style becomes <paramref name="changed"/> (its id stays); a built-in style that is not in the document yet is
    /// added first. Every paragraph and text that uses it changes with it.
    /// </summary>
    public void ModifyStyle(Style changed)
    {
        ArgumentNullException.ThrowIfNull(changed);
        Run($"Formatvorlage {BuiltInStyles.DisplayName(changed)} ändern", EditKind.Other, null, () =>
        {
            EnsureStyle(changed.Id);
            var sheet = Document.Styles;
            if (changed.BasedOn is { } basedOn)
            {
                sheet = BuiltInStyles.Ensure(sheet, basedOn);
            }

            Apply(DocumentSteps.Styles(Document, sheet.With(changed)));
        });
    }

    /// <summary>
    /// "Neue Formatvorlage": a style named <paramref name="name"/> (a paragraph style also formats the selected paragraphs, a character
    /// style the selected text, as in Word); returns its id. The name must be free (<see cref="StyleNameProblem"/>).
    /// </summary>
    public string CreateStyle(string name, StyleType type, string? basedOn, string? next, ParagraphProperties paragraph, CharacterProperties character, bool quickFormat = true)
    {
        if (StyleNameProblem(name) is { } problem)
        {
            throw new ArgumentException(problem, nameof(name));
        }

        var id = NewStyleId(name.Trim());
        var style = new Style
        {
            Id = id,
            Name = name.Trim(),
            Type = type,
            BasedOn = basedOn,
            Next = type == StyleType.Paragraph ? next ?? id : null,
            IsCustom = true,
            QuickFormat = quickFormat,
            Paragraph = type == StyleType.Paragraph ? paragraph with { StyleId = null } : ParagraphProperties.Empty,
            Character = character with { StyleId = null },
        };
        Run($"Formatvorlage {style.Name}", EditKind.Other, null, () =>
        {
            var sheet = Document.Styles;
            if (basedOn is not null)
            {
                sheet = BuiltInStyles.Ensure(sheet, basedOn);
            }

            if (next is not null && next != id)
            {
                sheet = BuiltInStyles.Ensure(sheet, next);
            }

            Apply(DocumentSteps.Styles(Document, sheet.With(style)));
            if (type == StyleType.Paragraph)
            {
                FormatSelectedParagraphs(item => item.WithProperties(item.Properties with { StyleId = id }));
            }
            else if (!_selection.IsCollapsed)
            {
                foreach (var range in TableSelection.Ranges(Document, _selection))
                {
                    ApplyOptional(ContentEdits.FormatCharacters(Document, range, (_, props) => props with { StyleId = id }));
                }
            }
        });
        return id;
    }

    /// <summary>
    /// "Auswahl als neue Schnellformatvorlage": a paragraph style with the format of the selection – its paragraph's direct paragraph
    /// format and the direct character format where the selection starts – based on the paragraph's style; the selected paragraphs get
    /// it. Returns its id.
    /// </summary>
    public string StyleFromSelection(string name)
    {
        var paragraph = Document.ParagraphAt(_selection.Start.Block) ?? Paragraph.Empty();
        var basedOn = Resolver.ParagraphStyleId(paragraph);
        var characters = paragraph.Length == 0
            ? paragraph.MarkProperties
            : paragraph.InlineAt(Math.Min(_selection.Start.Offset, paragraph.Length - 1))?.Properties ?? paragraph.MarkProperties;
        var paragraphProperties = paragraph.Properties with { StyleId = null, Numbering = null, UnknownXml = null };
        return CreateStyle(name, StyleType.Paragraph, string.IsNullOrEmpty(basedOn) ? null : basedOn, null, paragraphProperties, characters with { UnknownXml = null, StyleId = null });
    }

    /// <summary>An id for a new style from its name (letters and digits, unique in the document).</summary>
    private string NewStyleId(string name)
    {
        var builder = new StringBuilder();
        foreach (var character in name.Normalize(NormalizationForm.FormC))
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        var stem = builder.Length == 0 ? "Formatvorlage" : builder.ToString();
        var id = stem;
        for (var number = 2; Document.Styles.Find(id) is not null || BuiltInStyles.Definition(id) is not null; number++)
        {
            id = stem + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return id;
    }
}

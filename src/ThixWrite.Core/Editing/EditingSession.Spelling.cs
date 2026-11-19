using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>Überprüfen › Sprache and the corrections of the spelling check.</summary>
public sealed partial class EditingSession
{
    /// <summary>The language of the document's text (its default w:lang; "de-DE" when it names none).</summary>
    public string Language => DocumentLanguage.Of(Document);

    /// <summary>Sprache: the document's text is in <paramref name="language"/> (saved with the document; one step).</summary>
    public void SetLanguage(string language)
    {
        if (string.Equals(Language, language, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Run("Sprache", EditKind.Other, null, () =>
        {
            var styles = Document.Styles;
            Apply(DocumentSteps.Styles(Document, styles.WithDefaults(styles.ParagraphDefaults, DocumentLanguage.With(styles.CharacterDefaults, language))));
        });
    }

    /// <summary>A suggestion of the spelling check: the word in <paramref name="range"/> becomes <paramref name="replacement"/> (one step, in its formatting).</summary>
    public void ReplaceWord(DocumentRange range, string replacement)
    {
        if (range.IsEmpty || range.IsMultiBlock || Document.ParagraphAt(range.Start.Block) is not { } paragraph)
        {
            return;
        }

        Run("Rechtschreibung", EditKind.Other, null, () =>
        {
            var properties = paragraph.InlineAt(range.Start.Offset)?.Properties ?? paragraph.MarkProperties;
            var link = paragraph.InlineAt(range.Start.Offset)?.Link;
            var map = (ReplaceMap)Apply(ContentEdits.InsertText(Document, range, replacement, properties, link));
            SetSelectionQuiet(Selection.Caret(map.EndOfInserted));
        });
    }
}

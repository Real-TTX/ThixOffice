using Avalonia.Controls;
using ThixOffice.Desktop.Controls;
using ThixShow.Core.Editing;

namespace ThixShow.Editor;

/// <summary>
/// Suchen / Ersetzen of ThixShow on the shared bar (<see cref="FindBarBase"/>) with PowerPoint's options: Groß-/Kleinschreibung,
/// Nur ganzes Wort suchen, Notizen durchsuchen. Searches the slides' shapes and table cells in order and wraps around; the hit is
/// selected as text on its slide.
/// </summary>
internal sealed class ShowFindBar : FindBarBase
{
    private readonly ShowEditor _editor;

    public ShowFindBar(ShowEditor editor)
        : this(editor, new CheckBox { Content = "Groß-/Kleinschreibung" }, new CheckBox { Content = "Nur ganzes Wort suchen" }, new CheckBox { Content = "Notizen durchsuchen" })
    {
    }

    private ShowFindBar(ShowEditor editor, CheckBox matchCase, CheckBox wholeWords, CheckBox notes)
        : base("Suchen nach", matchCase, wholeWords, notes)
    {
        _editor = editor;
        MatchCase = matchCase;
        WholeWords = wholeWords;
        IncludeNotes = notes;
        matchCase.IsCheckedChanged += (_, _) => SearchChanged();
        wholeWords.IsCheckedChanged += (_, _) => SearchChanged();
        notes.IsCheckedChanged += (_, _) => SearchChanged();
    }

    public CheckBox MatchCase { get; }

    public CheckBox WholeWords { get; }

    public CheckBox IncludeNotes { get; }

    private FindOptions? Options() => SearchText is { } text
        ? new FindOptions(text, MatchCase.IsChecked == true, WholeWords.IsChecked == true, IncludeNotes.IsChecked == true)
        : null;

    public override bool FindNext(bool forward = true)
    {
        if (Options() is not { } options)
        {
            return false;
        }

        var found = _editor.Session.FindNext(options, forward);
        UpdateCount();
        return found;
    }

    public override void ReplaceOne()
    {
        if (Options() is { } options)
        {
            _ = _editor.RunAsync("Ersetzen", () => _editor.Session.Replace(options, ReplacementText));
            UpdateCount();
        }
    }

    protected override int ReplaceEverything()
    {
        if (Options() is not { } options)
        {
            return 0;
        }

        var count = 0;
        _ = _editor.RunAsync("Alle ersetzen", () => count = _editor.Session.ReplaceAll(options, ReplacementText));
        return count;
    }

    protected override string? SelectedSearchText()
    {
        var session = _editor.Session;
        if (session.Selection.Text is not { IsCollapsed: false } text || session.TextBodyOf(text.Target) is not { } body || text.Start.Paragraph != text.End.Paragraph)
        {
            return null;
        }

        var paragraph = body.Paragraphs[text.Start.Paragraph].Text;
        return paragraph[Math.Min(text.Start.Offset, paragraph.Length)..Math.Min(text.End.Offset, paragraph.Length)];
    }

    protected override void SearchChanged() => UpdateCount();

    protected override void UpdateCount()
    {
        if (Options() is not { } options)
        {
            SetCount("");
            return;
        }

        var hits = _editor.Session.CountMatches(options);
        SetCount(hits == 0 ? "Keine Treffer" : hits == 1 ? "1 Treffer" : $"{hits} Treffer");
    }

    protected override void FocusDocument() => _editor.FocusDocument();
}

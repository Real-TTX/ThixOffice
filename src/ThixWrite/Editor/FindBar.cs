using Avalonia.Controls;
using ThixOffice.Desktop.Controls;
using ThixWrite.Core.Editing;

namespace ThixWrite.Editor;

/// <summary>
/// Suchen / Ersetzen of ThixWrite on the shared bar (<see cref="FindBarBase"/>): text in the document's paragraphs, with the
/// options Groß-/Kleinschreibung and Nur ganze Wörter. The current hit is the document's selection (highlighted, scrolled
/// into view).
/// </summary>
public sealed class FindBar : FindBarBase
{
    private readonly WriteEditor _editor;

    public FindBar(WriteEditor editor)
        : this(editor, new CheckBox { Content = "Groß-/Kleinschreibung" }, new CheckBox { Content = "Nur ganze Wörter" })
    {
    }

    private FindBar(WriteEditor editor, CheckBox matchCase, CheckBox wholeWord)
        : base("Im Dokument suchen", matchCase, wholeWord)
    {
        _editor = editor;
        MatchCase = matchCase;
        WholeWord = wholeWord;
        matchCase.IsCheckedChanged += (_, _) => SearchChanged();
        wholeWord.IsCheckedChanged += (_, _) => SearchChanged();
    }

    public CheckBox MatchCase { get; }

    public CheckBox WholeWord { get; }

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
            _editor.Session.Replace(options, ReplacementText);
            UpdateCount();
        }
    }

    protected override int ReplaceEverything() => Options() is { } options ? _editor.Session.ReplaceAll(options, ReplacementText) : -1;

    protected override string? SelectedSearchText()
    {
        var selection = _editor.Session.Selection;
        return !selection.IsCollapsed && selection.Range is { IsMultiBlock: false } range && _editor.Session.Document.ParagraphAt(range.Start.Block) is { } paragraph
            ? paragraph.Text[range.Start.Offset..range.End.Offset]
            : null;
    }

    protected override void FocusDocument() => _editor.FocusDocument();

    private FindOptions? Options() => SearchText is { } text ? new FindOptions(text, MatchCase?.IsChecked == true, WholeWord?.IsChecked == true) : null;

    /// <summary>Typing in the search field: the first hit from where the selection starts shows at once.</summary>
    protected override void SearchChanged()
    {
        if (!IsVisible || Options() is not { } options)
        {
            UpdateCount();
            return;
        }

        var session = _editor.Session;
        var start = session.Selection.Start;
        if (TextSearch.IsMatch(session.Document, session.Selection.Range, options))
        {
            UpdateCount();
            return;
        }

        session.SetCaret(start);
        session.FindNext(options);
        UpdateCount();
    }

    protected override void UpdateCount()
    {
        if (Options() is not { } options)
        {
            SetCount("");
            return;
        }

        var session = _editor.Session;
        var hits = TextSearch.FindAll(session.Document, options);
        var current = hits.ToList().FindIndex(hit => hit.Start == session.Selection.Start && hit.End == session.Selection.End);
        SetCount(CountOf(hits.Count, current));
    }
}

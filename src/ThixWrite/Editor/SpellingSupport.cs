using Avalonia.Threading;
using ThixOffice.Core.Business;
using ThixOffice.Core.Spelling;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// The spelling check of one editor: the system's checker for the document's language (<see cref="SpellingService"/>), a
/// <see cref="SpellingQueue"/> that checks the paragraphs of the visible pages in the background (each paragraph once – they are
/// immutable, a typed one is new), the ranges of unknown words for the red wavy lines (a layer: no page is drawn again), the word
/// under the caret for the context menu and the next unknown word for Rechtschreibung (F7). Off when the setting is off or the system
/// has no checker for any language.
/// </summary>
internal sealed class SpellingSupport : IDisposable
{
    private readonly WriteEditor _editor;
    private readonly Action _redraw;
    private SpellingQueue? _queue;
    private string? _language;
    private bool _redrawPending;

    public SpellingSupport(WriteEditor editor, Action redraw)
    {
        _editor = editor;
        _redraw = redraw;
        SpellingService.WordsChanged += OnWordsChanged;
        SettingsService.Changed += OnSettingsChanged;
    }

    /// <summary>True when unknown words are underlined while the user types.</summary>
    public bool IsOn => SettingsService.Get().CheckSpellingAsYouType && Checker is not null;

    /// <summary>The checker of the document's language (or the nearest the system has), or null.</summary>
    public ISpellChecker? Checker => EnsureQueue()?.Checker;

    /// <summary>The ranges of unknown words in the paragraphs on <paramref name="pages"/> that are known now (the rest is being checked).</summary>
    public IReadOnlyList<DocumentRange> Misspelled(IReadOnlyList<int> pages)
    {
        if (!SettingsService.Get().CheckSpellingAsYouType || EnsureQueue() is not { } queue)
        {
            return [];
        }

        var document = _editor.Document.Content;
        var layout = _editor.Layout;
        var blocks = new SortedSet<int>();
        foreach (var page in pages)
        {
            if (page < layout.PageCount)
            {
                foreach (var line in layout.Pages[page].Lines)
                {
                    blocks.Add(line.Block);
                }
            }
        }

        var selection = _editor.Document.Session.Selection;
        var caret = selection.IsCollapsed ? selection.Active : new DocumentPosition(-1, -1);
        var ranges = new List<DocumentRange>();
        foreach (var block in blocks)
        {
            if (document.ParagraphAt(block) is not { Length: > 0 } paragraph)
            {
                continue;
            }

            var text = SpellingText.Of(paragraph);
            if (!queue.TryGet(paragraph, text.Text, out var errors))
            {
                continue;
            }

            foreach (var error in errors)
            {
                var (start, end) = text.ToParagraph(error.Start, error.Length);
                if (block == caret.Block && end == caret.Offset)
                {
                    continue; // the word being typed is not marked yet (as in Word)
                }

                ranges.Add(new DocumentRange(new DocumentPosition(block, start), new DocumentPosition(block, end)));
            }
        }

        return ranges;
    }

    /// <summary>The unknown word at <paramref name="position"/> (its range and text), or null.</summary>
    public (DocumentRange Range, string Word)? ErrorAt(DocumentPosition position)
    {
        if (!IsOn || _editor.Document.Content.ParagraphAt(position.Block) is not { Length: > 0 } paragraph)
        {
            return null;
        }

        return Errors(position.Block, paragraph).FirstOrDefault(error => error.Range.Start.Offset <= position.Offset && position.Offset <= error.Range.End.Offset) is { Word: not null } found
            ? found
            : null;
    }

    /// <summary>The first unknown word at or after <paramref name="from"/> (to the end of the document), or null.</summary>
    public (DocumentRange Range, string Word)? NextError(DocumentPosition from)
    {
        if (Checker is null)
        {
            return null;
        }

        var document = _editor.Document.Content;
        for (var block = from.Block; block < document.Blocks.Count; block++)
        {
            if (document.ParagraphAt(block) is not { Length: > 0 } paragraph)
            {
                continue;
            }

            foreach (var error in Errors(block, paragraph))
            {
                if (block > from.Block || error.Range.Start.Offset >= from.Offset)
                {
                    return error;
                }
            }
        }

        return null;
    }

    /// <summary>Checks the paragraphs of <paramref name="pages"/> now (snapshots: the lines are there when the picture is taken).</summary>
    public void CheckNow(IReadOnlyList<int> pages)
    {
        if (EnsureQueue() is not { } queue)
        {
            return;
        }

        foreach (var page in pages.Where(page => page < _editor.Layout.PageCount))
        {
            foreach (var block in _editor.Layout.Pages[page].Lines.Select(line => line.Block).Distinct())
            {
                if (_editor.Document.Content.ParagraphAt(block) is { Length: > 0 } paragraph)
                {
                    queue.CheckNow(paragraph, SpellingText.Of(paragraph).Text);
                }
            }
        }
    }

    public void Dispose()
    {
        SpellingService.WordsChanged -= OnWordsChanged;
        SettingsService.Changed -= OnSettingsChanged;
        _queue?.Dispose();
    }

    /// <summary>The unknown words of a paragraph, checked now when the queue has no result yet.</summary>
    private IEnumerable<(DocumentRange Range, string Word)> Errors(int block, Paragraph paragraph)
    {
        if (EnsureQueue() is not { } queue)
        {
            yield break;
        }

        var text = SpellingText.Of(paragraph);
        var errors = queue.CheckNow(paragraph, text.Text);

        foreach (var error in errors)
        {
            var (start, end) = text.ToParagraph(error.Start, error.Length);
            yield return (new DocumentRange(new DocumentPosition(block, start), new DocumentPosition(block, end)), text.Text.Substring(error.Start, error.Length));
        }
    }

    /// <summary>The queue of the document's language (made anew when the language changed).</summary>
    private SpellingQueue? EnsureQueue()
    {
        var language = _editor.Document.Session.Language;
        if (_queue is not null && language == _language)
        {
            return _queue;
        }

        _queue?.Dispose();
        _queue = null;
        _language = language;
        if (SpellingService.CheckerFor(language) is { } checker)
        {
            _queue = new SpellingQueue(checker);
            _queue.Checked += (_, _) => Redraw();
        }

        return _queue;
    }

    private void OnWordsChanged(object? sender, EventArgs e)
    {
        _queue?.Clear();
        Redraw();
    }

    private void OnSettingsChanged(object? sender, ThixOffice.Core.Models.AppSettings e) => Redraw();

    /// <summary>Draws again on the UI thread, once however many results came.</summary>
    private void Redraw()
    {
        if (_redrawPending)
        {
            return;
        }

        _redrawPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _redrawPending = false;
            _redraw();
        }, DispatcherPriority.Background);
    }
}

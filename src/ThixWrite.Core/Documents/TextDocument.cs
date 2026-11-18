using System.Text;
using ThixOffice.Core.Documents;

namespace ThixWrite.Core.Documents;

/// <summary>
/// PHASE 0 ONLY: the trivial document of ThixWrite – plain text, UTF-8 – so that new, open, save, save as, recent, recovery,
/// the unsaved prompt and file activation are used for real. Typing that follows quickly is one undo step (like a word
/// processor). The real model (sections, paragraphs, runs, styles) replaces it in phase 1a.
/// </summary>
public sealed class TextDocument(string untitledName) : OfficeDocument(untitledName)
{
    /// <summary>How many undo steps are kept.</summary>
    public const int UndoLimit = 200;

    /// <summary>Changes closer together than this (typing) are one undo step.</summary>
    public static readonly TimeSpan TypingPause = TimeSpan.FromSeconds(1);

    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private DateTime _lastTypedAt = DateTime.MinValue;

    public string Text { get; private set; } = "";

    public override bool CanUndo => _undo.Count > 0;

    public override bool CanRedo => _redo.Count > 0;

    /// <summary>The text after an edit. <paramref name="isTyping"/>: joins the undo step of the typing just before.</summary>
    public void SetText(string text, bool isTyping = false, DateTime? now = null)
    {
        if (text == Text)
        {
            return;
        }

        var at = now ?? DateTime.UtcNow;
        var joinsTyping = isTyping && _undo.Count > 0 && at - _lastTypedAt < TypingPause;
        if (!joinsTyping)
        {
            _undo.Add(Text);
            if (_undo.Count > UndoLimit)
            {
                _undo.RemoveAt(0);
            }
        }

        _lastTypedAt = isTyping ? at : DateTime.MinValue;
        _redo.Clear();
        Text = text;
        OnContentChanged();
    }

    /// <summary>Puts in a content without an undo step and without an unsaved change (sample documents, templates).</summary>
    public void Load(string text)
    {
        Text = text;
        _undo.Clear();
        _redo.Clear();
        RaiseChanged();
    }

    /// <summary>Inserts <paramref name="value"/> at <paramref name="position"/> (one undo step); returns the position after it.</summary>
    public int Insert(int position, string value)
    {
        var at = Math.Clamp(position, 0, Text.Length);
        SetText(Text.Insert(at, value));
        return at + value.Length;
    }

    public override void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(Text);
        Text = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _lastTypedAt = DateTime.MinValue;
        OnContentChanged();
    }

    public override void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(Text);
        Text = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _lastTypedAt = DateTime.MinValue;
        OnContentChanged();
    }

    /// <summary>Words and characters, for the status bar.</summary>
    public (int Words, int Characters) Count() =>
        (Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length, Text.Count(character => !char.IsControl(character)));

    protected override void Read(Stream stream)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false), detectEncodingFromByteOrderMarks: true);
        Text = reader.ReadToEnd();
        _undo.Clear();
        _redo.Clear();
    }

    protected override void Write(Stream stream)
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(Text);
        stream.Write(bytes);
    }
}

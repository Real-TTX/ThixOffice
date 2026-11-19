namespace ThixWrite.Core.Editing;

/// <summary>What kind of edit a transaction is (typing and deleting group into one undo step while they continue).</summary>
public enum EditKind
{
    Other,
    Typing,
    DeleteBackward,
    DeleteForward,
}

/// <summary>One undo step: the steps of a command, the selection before and after it, a German label ("Eingabe").</summary>
internal sealed class EditTransaction(string label, EditKind kind, Selection before)
{
    public string Label { get; } = label;

    public EditKind Kind { get; } = kind;

    public List<EditStep> Steps { get; } = [];

    public Selection Before { get; } = before;

    public Selection After { get; set; } = before;

    public DateTime LastEdit { get; set; }

    /// <summary>The block the typing of this transaction happens in (grouping stops when the caret leaves it).</summary>
    public int Block { get; set; } = -1;
}

/// <summary>The undo and redo stacks of an editing session.</summary>
internal sealed class UndoHistory
{
    public const int Limit = 1000;

    private readonly LinkedList<EditTransaction> _undo = new();
    private readonly Stack<EditTransaction> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => _undo.Last?.Value.Label;

    public string? RedoLabel => _redo.Count > 0 ? _redo.Peek().Label : null;

    /// <summary>The last transaction, while it may still take more typing.</summary>
    public EditTransaction? Last => _undo.Last?.Value;

    public void Push(EditTransaction transaction)
    {
        _undo.AddLast(transaction);
        if (_undo.Count > Limit)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
    }

    public EditTransaction? PopUndo()
    {
        var last = _undo.Last;
        if (last is null)
        {
            return null;
        }

        _undo.RemoveLast();
        _redo.Push(last.Value);
        return last.Value;
    }

    public EditTransaction? PopRedo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var transaction = _redo.Pop();
        _undo.AddLast(transaction);
        return transaction;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}

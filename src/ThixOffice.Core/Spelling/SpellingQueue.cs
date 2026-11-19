using System.Runtime.CompilerServices;
using ThixOffice.Core.Infrastructure;

namespace ThixOffice.Core.Spelling;

/// <summary>
/// Checks texts in the background and remembers the results by their owner – an immutable object such as a paragraph – so a view
/// asks for what it shows (<see cref="TryGet"/>: the result when known, else the text is queued) and draws again when
/// <see cref="Checked"/> says that results came. The latest requests go first (what is on the screen now). The owners are held weakly.
/// </summary>
public sealed class SpellingQueue : IDisposable
{
    private readonly ConditionalWeakTable<object, Result> _results = new();
    private readonly Stack<(WeakReference<object> Owner, string Text)> _pending = new();
    private readonly HashSet<object> _queued = new(ReferenceEqualityComparer.Instance);
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _lock = new();
    private int _generation;

    public SpellingQueue(ISpellChecker checker)
    {
        Checker = checker;
        _ = Task.Run(RunAsync);
    }

    /// <summary>Raised (on a worker thread) after results came in.</summary>
    public event EventHandler? Checked;

    public ISpellChecker Checker { get; }

    /// <summary>The unknown words of <paramref name="owner"/>'s <paramref name="text"/> when they are known; else false and the text is queued.</summary>
    public bool TryGet(object owner, string text, out IReadOnlyList<SpellingError> errors)
    {
        lock (_lock)
        {
            if (_results.TryGetValue(owner, out var result) && result.Generation == _generation && string.Equals(result.Text, text, StringComparison.Ordinal))
            {
                errors = result.Errors;
                return true;
            }

            errors = [];
            if (_queued.Add(owner))
            {
                _pending.Push((new WeakReference<object>(owner), text));
                _signal.Release();
            }

            return false;
        }
    }

    /// <summary>Checks a text now, on the calling thread, and remembers the result (snapshots, F7 on a paragraph not seen yet).</summary>
    public IReadOnlyList<SpellingError> CheckNow(object owner, string text)
    {
        if (TryGet(owner, text, out var known))
        {
            return known;
        }

        var errors = Checker.Check(text);
        lock (_lock)
        {
            _results.AddOrUpdate(owner, new Result(text, errors, _generation));
        }

        return errors;
    }

    /// <summary>Forgets every result (words were ignored or added): everything is checked again when asked.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _generation++;
            _queued.Clear();
            _pending.Clear();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _signal.Release();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await _signal.WaitAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var any = false;
            while (Next() is { } item)
            {
                var (owner, text, generation) = item;
                IReadOnlyList<SpellingError> errors;
                try
                {
                    errors = Checker.Check(text);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    Log.Warning($"Spelling check failed: {exception.GetType().Name}");
                    errors = [];
                }

                lock (_lock)
                {
                    _queued.Remove(owner);
                    if (generation == _generation)
                    {
                        _results.AddOrUpdate(owner, new Result(text, errors, generation));
                        any = true;
                    }
                }

                if (_stop.IsCancellationRequested)
                {
                    return;
                }
            }

            if (any)
            {
                Checked?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private (object Owner, string Text, int Generation)? Next()
    {
        lock (_lock)
        {
            while (_pending.Count > 0)
            {
                var (reference, text) = _pending.Pop();
                if (reference.TryGetTarget(out var owner))
                {
                    return (owner, text, _generation);
                }
            }

            return null;
        }
    }

    private sealed record Result(string Text, IReadOnlyList<SpellingError> Errors, int Generation);
}

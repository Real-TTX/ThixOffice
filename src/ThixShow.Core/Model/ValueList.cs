using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace ThixShow.Core.Model;

/// <summary>
/// An immutable list with value equality, so that records holding lists (shapes, paragraphs, stops) compare by content and an
/// unchanged slide equals the slide it was read as. The "With…" methods return a new list (structural sharing is left to the
/// records above: a slide that did not change keeps its instance).
/// </summary>
[CollectionBuilder(typeof(ValueList), nameof(ValueList.Create))]
public readonly struct ValueList<T> : IEquatable<ValueList<T>>, IReadOnlyList<T>
{
    private readonly ImmutableArray<T> _items;

    public ValueList(ImmutableArray<T> items) => _items = items;

    public ValueList(IEnumerable<T> items) => _items = [.. items];

    public static ValueList<T> Empty => new(ImmutableArray<T>.Empty);

    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Count => Items.Length;

    public bool IsEmpty => Count == 0;

    public T this[int index] => Items[index];

    public static bool operator ==(ValueList<T> left, ValueList<T> right) => left.Equals(right);

    public static bool operator !=(ValueList<T> left, ValueList<T> right) => !left.Equals(right);

    public static implicit operator ValueList<T>(T[] items) => new(ImmutableArray.Create(items));

    public static implicit operator ValueList<T>(List<T> items) => new(items);

    public ValueList<T> SetItem(int index, T item) => new(Items.SetItem(index, item));

    public ValueList<T> Add(T item) => new(Items.Add(item));

    public ValueList<T> AddRange(IEnumerable<T> items) => new(Items.AddRange(items));

    public ValueList<T> Insert(int index, T item) => new(Items.Insert(index, item));

    public ValueList<T> InsertRange(int index, IEnumerable<T> items) => new(Items.InsertRange(index, items));

    public ValueList<T> RemoveAt(int index) => new(Items.RemoveAt(index));

    public ValueList<T> RemoveRange(int index, int count) => new(Items.RemoveRange(index, count));

    public ValueList<T> RemoveAll(Predicate<T> match) => new(Items.RemoveAll(match));

    public int IndexOf(T item) => Items.IndexOf(item);

    /// <summary>True when both lists share the same storage (cheap "unchanged" test, no element comparison).</summary>
    public bool IsSameInstance(ValueList<T> other) => Items == other.Items;

    public int FindIndex(Func<T, bool> match)
    {
        for (var index = 0; index < Count; index++)
        {
            if (match(Items[index]))
            {
                return index;
            }
        }

        return -1;
    }

    public bool Equals(ValueList<T> other) =>
        Items == other.Items || Items.SequenceEqual(other.Items, EqualityComparer<T>.Default);

    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Count);
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    public override string ToString() => $"[{string.Join(", ", Items)}]";
}

/// <summary>Builder of <see cref="ValueList{T}"/> for collection expressions.</summary>
public static class ValueList
{
    public static ValueList<T> Create<T>(ReadOnlySpan<T> items) => new(ImmutableArray.Create(items));

    public static ValueList<T> From<T>(IEnumerable<T> items) => new(items);
}

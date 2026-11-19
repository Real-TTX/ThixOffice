using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace ThixWrite.Core.Model;

/// <summary>An immutable array with value equality, so that records holding lists (tab stops, levels) compare by content.</summary>
[CollectionBuilder(typeof(EquatableArray), nameof(EquatableArray.Create))]
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly ImmutableArray<T> _items;

    public EquatableArray(ImmutableArray<T> items) => _items = items;

    public EquatableArray(IEnumerable<T> items) => _items = [.. items];

    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    public ImmutableArray<T> Items => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public int Count => Items.Length;

    public bool IsEmpty => Count == 0;

    public T this[int index] => Items[index];

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public bool Equals(EquatableArray<T> other) => Items.SequenceEqual(other.Items);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
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

/// <summary>Builder of <see cref="EquatableArray{T}"/> for collection expressions.</summary>
public static class EquatableArray
{
    public static EquatableArray<T> Create<T>(ReadOnlySpan<T> items) => new(ImmutableArray.Create(items));
}

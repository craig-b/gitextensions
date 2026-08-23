namespace GitUI.UserControls.RevisionGrid.Graph;

/// <summary>
///  An append-only list safe for one writer and concurrent lock-free readers: the element is
///  stored BEFORE the count is published (volatile release store), and on growth the new array is
///  published before any count that references its slots. A reader that reads <see cref="Count"/>
///  (volatile acquire) can therefore index any position below it and always sees a fully written
///  element.
///  <para>
///  This is exactly the property <see cref="List{T}"/> lacks: its Add publishes the new size
///  before writing the slot, so a concurrent reader can observe the grown Count while the last
///  element is still null — which is how the render thread crashed against the graph's cache
///  pump.
///  </para>
///  <para>Writes (<see cref="Add"/>, <see cref="EnsureCapacity"/>) must come from a single writer
///  at a time; reads need no coordination.</para>
/// </summary>
internal sealed class AppendOnlyList<T> : IReadOnlyList<T>
    where T : class
{
    private T[] _items;
    private int _count;

    public AppendOnlyList(int capacity = 0)
    {
        _items = capacity > 0 ? new T[capacity] : [];
    }

    public int Count => Volatile.Read(ref _count);

    /// <summary>
    ///  A possibly newer array than the one the caller's Count was published against is always
    ///  safe: arrays only grow and every replacement carries all published elements.
    /// </summary>
    public T this[int index] => Volatile.Read(ref _items)[index];

    public void Add(T item)
    {
        int count = _count;
        if (count == _items.Length)
        {
            Grow(Math.Max(4, _items.Length * 2));
        }

        _items[count] = item;
        Volatile.Write(ref _count, count + 1);
    }

    public void EnsureCapacity(int capacity)
    {
        if (capacity > _items.Length)
        {
            Grow(capacity);
        }
    }

    private void Grow(int capacity)
    {
        T[] grown = new T[capacity];
        Array.Copy(_items, grown, _count);

        // Publish the array before any count that references its new slots.
        Volatile.Write(ref _items, grown);
    }

    public IEnumerator<T> GetEnumerator()
    {
        int count = Count;
        for (int index = 0; index < count; index++)
        {
            yield return this[index];
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

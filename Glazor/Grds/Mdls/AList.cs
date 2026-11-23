using Glazor.Misc;
using System.Collections;

namespace Glazor.Grds.Mdls;

/// <summary>
/// Abstract list that notifies on changes.
/// </summary>
public abstract class AList<T> : IList<T>, IReadOnlyList<T>
{
    private IList<T> _list;

    /// <summary>
    /// Just a wrapper to the list: it is not copied, unless it is read-only or fixed-size, as an array.
    /// </summary>
    protected AList(IList<T> list)
    {
        this._list = Writable(list.RequireNotNull());
    }

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>The change every write signals: <paramref name="newItem"/> is the item added or inserted, default for any other change.</summary>
    protected abstract void OnChange(T? newItem);

    /// <summary>The change <see cref="SetAndGoto"/> signals: <paramref name="item"/> was asked for by name, not only added.</summary>
    protected abstract void OnGoto(T? item);

    /// <inheritdoc/>
    public T this[int index]
    {
        get => _list[index];
        set
        {
            OnChange(default);
            _list[index] = value;
        }
    }

    /// <inheritdoc/>
    public int Count => _list.Count;

    /// <summary>Appends the item at the end; once the model has synced, it becomes the current row, as an inserted one does.</summary>
    public void Add(T item)
    {
        OnChange(item);
        _list.Add(item);
    }

    /// <summary>Appends the items at the end with a single change notice; unlike <see cref="Add"/>, it leaves the current row where it is.</summary>
    public void AddRange(IEnumerable<T> items)
    {
        OnChange(default);
        if (_list is List<T> list)
        {
            list.AddRange(items);
            return;
        }
        foreach (T item in items)
        {
            _list.Add(item);
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        OnChange(default);
        _list.Clear();
    }

    /// <summary>
    /// Wraps the given list.
    /// </summary>
    /// <param name="items">The list to wrap. Not copied — the caller's instance becomes the backing store — unless it is read-only or fixed-size, as an array.</param>
    public void Set(IList<T> items)
    {
        if (Object.ReferenceEquals(this, items))
            throw new ArgumentException("A list cannot wrap itself.", nameof(items));
        OnChange(default);
        _list = Writable(items);
    }

    /// <summary>
    /// Wraps the given list.
    /// </summary>
    /// <param name="items">The list to wrap. Not copied — the caller's instance becomes the backing store — unless it is read-only or fixed-size, as an array.</param>
    /// <param name="item">Tries to restore the current element</param>
    public void SetAndGoto(IList<T> items, T? item)
    {
        if (Object.ReferenceEquals(this, items))
            throw new ArgumentException("A list cannot wrap itself.", nameof(items));
        OnGoto(item);
        _list = Writable(items);
    }

    // an array reports IsReadOnly and cannot take the insert an edit posts
    private static IList<T> Writable(IList<T> items) => items.IsReadOnly ? [.. items] : items;

    /// <inheritdoc/>
    public bool Contains(T item) => _list.Contains(item);

    /// <inheritdoc/>
    public void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

    /// <inheritdoc/>
    public int IndexOf(T item) => _list.IndexOf(item);

    /// <inheritdoc/>
    public void Insert(int index, T item)
    {
        OnChange(item);
        _list.Insert(index, item);
    }

    /// <inheritdoc/>
    public bool Remove(T item)
    {
        int index = _list.IndexOf(item);   // a miss changes nothing; a hit signals before the list changes, as the others do
        if (index < 0)
            return false;
        OnChange(default);
        _list.RemoveAt(index);
        return true;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        OnChange(default);
        _list.RemoveAt(index);
    }

    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();

    /// <summary>The first item <paramref name="match"/> accepts, in list order; default when none does.</summary>
    public T? Find(Predicate<T> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        for (int i = 0; i < Count; i++)
        {
            if (match(this[i]))
            {
                return this[i];
            }
        }
        return default;
    }
}

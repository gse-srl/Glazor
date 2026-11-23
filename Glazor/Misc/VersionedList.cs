using System.Collections;

namespace Glazor.Misc;

/// <summary>
/// A list that keeps track of changes on its elements, any time a change happens the Version is incremented.
/// </summary>
public class VersionedList<T> : IList<T>, IReadOnlyList<T>
{
    private int _version;

    private IList<T> _list;
    private readonly bool _trackUpdates;

    private readonly IEqualityComparer<T>? _comparer;

    /// <summary>
    /// Just a wrapper to the list, elements are not copied.
    /// </summary>
    /// <param name="list">The list wrapped; changes go to it.</param>
    /// <param name="trackUpdates">When false only inserts and removes are tracked, when true the element must implement a proper Equals() method</param>
    public VersionedList(IList<T> list, bool trackUpdates)
    {
        this._list = list.RequireNotNull();
        _trackUpdates = trackUpdates;
        _comparer = null;
    }

    /// <summary>Wraps <paramref name="list"/>, elements not copied; an indexer write counts as a change when <paramref name="comparer"/> finds the new element different.</summary>
    public VersionedList(IList<T> list, IEqualityComparer<T> comparer)
    {
        this._list = list.RequireNotNull();
        _trackUpdates = true;
        _comparer = comparer;
    }

    bool ICollection<T>.IsReadOnly => _list.IsReadOnly;

    private void Changed()
    {
        _version++;
    }

    /// <summary>Incremented on every change the list tracks.</summary>
    public int Version { get => _version; }

    /// <inheritdoc/>
    public T this[int index]
    {
        get => _list[index];
        set
        {
            var old = _list[index];
            _list[index] = value;
            if (_trackUpdates)
            {
                if (_comparer != null)
                {
                    if (!_comparer.Equals(old, value))
                        Changed();
                }
                else if (!Object.Equals(value, old))
                    Changed();
            }
        }
    }

    /// <inheritdoc/>
    public int Count => _list.Count;

    /// <inheritdoc/>
    public void Add(T item)
    {
        _list.Add(item);
        Changed();
    }

    /// <summary>Appends the items at the end.</summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (_list is List<T> list)
        {
            list.AddRange(items);
            Changed();
            return;
        }
        foreach (T item in items)
        {
            _list.Add(item);
            Changed();
        }
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _list.Clear();
        Changed();
    }

    /// <summary>
    /// Wraps the given list.
    /// </summary>
    /// <param name="items">The list to wrap. Not copied — the caller's instance becomes the backing store.</param>
    public void Set(IList<T> items)
    {
        if (Object.ReferenceEquals(this, items))
            throw new ArgumentException("A list cannot wrap itself.", nameof(items));
        _list = items.RequireNotNull();
        Changed();
    }

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
        _list.Insert(index, item);
        Changed();
    }

    /// <inheritdoc/>
    public bool Remove(T item)
    {
        bool res = _list.Remove(item);
        if (res)
            Changed();
        return res;
    }

    /// <inheritdoc/>
    public void RemoveAt(int index)
    {
        _list.RemoveAt(index);
        Changed();
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

    /// <summary>The index of the first item <paramref name="match"/> accepts; -1 when none does.</summary>
    public int FindIndex(Predicate<T> match)
    {
        ArgumentNullException.ThrowIfNull(match);
        for (int i = 0; i < Count; i++)
        {
            if (match(this[i]))
            {
                return i;
            }
        }
        return -1;
    }
}

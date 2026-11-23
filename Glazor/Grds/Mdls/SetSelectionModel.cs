using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Glazor.Grds.Mdls;

/// <summary>
/// Provides a selection model for grid items based on a set, allowing items to be selected or deselected and supporting
/// custom selection enablement logic.
/// With <c>defensiveCopy</c> true the model works on its own copy of the set passed in, with its comparer when it is a
/// <c>HashSet</c>, a <c>SortedSet</c>, an immutable or a frozen one, and the selection is read through <c>Selected()</c>.
/// With false it shares that set: the caller may read it, but changes the selection only through the model, which does not
/// see a change made to the set directly. A read-only set is copied either way, since the model could not select into it.
/// </summary>
/// <typeparam name="TGridItem">The type of items managed by the selection model.</typeparam>
public class SetSelectionModel<TGridItem> : ISelectionModel<TGridItem>
{
    /// <summary>The selection; shared with the caller when built without a defensive copy.</summary>
    protected readonly ISet<TGridItem> selected;
    /// <summary>Whether a row may change its state; a row it refuses keeps it.</summary>
    protected readonly Func<TGridItem, bool> selectionEnabledFn;

    private int _version;

    /// <summary>
    /// Changes any time the selection changes.
    /// </summary>
    public int Version => _version;

    /// <summary>
    /// Most common case, the selection set is owned and so no defensive copy is needed.
    /// </summary>
    public SetSelectionModel()
        : this(new HashSet<TGridItem>(), defensiveCopy: false)
    {
    }

    /// <summary>Starts from <paramref name="selected"/>, copied or shared as <paramref name="defensiveCopy"/> says.</summary>
    public SetSelectionModel(ISet<TGridItem> selected, bool defensiveCopy) : this(selected, defensiveCopy, _ => true)
    {
    }

    /// <summary>Starts from <paramref name="selected"/>, copied or shared as <paramref name="defensiveCopy"/> says; the rows <paramref name="selectionEnabledFn"/> refuses keep their state.</summary>
    public SetSelectionModel(ISet<TGridItem> selected, bool defensiveCopy, Func<TGridItem, bool> selectionEnabledFn)
    {
        this.selected = SelectedSet.Of(selected, defensiveCopy);
        this.selectionEnabledFn = selectionEnabledFn;
    }

    /// <summary>Records a change of the selection in <see cref="Version"/>.</summary>
    protected void IncrementVersion()
    {
        _version++;
    }

    /// <inheritdoc/>
    public int SelectedCount()
    {
        return selected.Count;
    }

    /// <summary>The items selected: the model's own set, not a copy; change the selection through the model.</summary>
    public IEnumerable<TGridItem> Selected()
    {
        return selected;
    }

    /// <summary>Deselects every item, the locked ones too.</summary>
    public void Clear()
    {
        if (selected.Any())
        {
            selected.Clear();
            IncrementVersion();
        }
    }

    /// <inheritdoc/>
    public bool IsSelected(TGridItem item)
    {
        return selected.Contains(item);
    }

    /// <inheritdoc/>
    public bool IsSelectionEnabled(TGridItem item)
    {
        return selectionEnabledFn(item);
    }

    /// <inheritdoc/>
    public void Select(TGridItem item, bool isSelected)
    {
        if (isSelected && !IsSelectionEnabled(item))
            return;   // a locked row keeps its state
        if (isSelected)
        {
            if (selected.Add(item))
                IncrementVersion();
        }
        else
        {
            if (selected.Remove(item))
                IncrementVersion();
        }
    }
}

/// <summary>
/// Provides a selection model for grid items using a set of keys to track selected items.
/// With <c>defensiveCopy</c> true the model works on its own copy of the set passed in, with its comparer when it is a
/// <c>HashSet</c>, a <c>SortedSet</c>, an immutable or a frozen one, and the selection is read through <c>Selected()</c>.
/// With false it shares that set: the caller may read it, but changes the selection only through the model, which does not
/// see a change made to the set directly. A read-only set is copied either way, since the model could not select into it.
/// </summary>
/// <typeparam name="TGridItem">The type of items in the grid to be managed by the selection model.</typeparam>
/// <typeparam name="KT">The type of key used to uniquely identify each grid item.</typeparam>
public class SetSelectionModel<TGridItem, KT> : ISelectionModel<TGridItem>
{
    /// <summary>The selection; shared with the caller when built without a defensive copy.</summary>
    protected readonly ISet<KT> selected;
    /// <summary>Whether a row may change its state; a row it refuses keeps it.</summary>
    protected readonly Func<TGridItem, bool> selectionEnabledFn;

    /// <summary>The key of an item: the selection holds keys.</summary>
    public Func<TGridItem, KT> ValueToKeyFn { get; }

    private int _version;

    /// <summary>
    /// Changes any time the selection changes.
    /// </summary>
    public int Version => _version;

    /// <summary>
    /// Most common case, the selection set is owned and so no defensive copy is needed.
    /// </summary>
    /// <param name="valueToKeyFn">The key of an item: the selection holds keys.</param>
    public SetSelectionModel(Func<TGridItem, KT> valueToKeyFn)
        : this(valueToKeyFn, new HashSet<KT>(), defensiveCopy: false)
    {
    }

    /// <summary>Starts from <paramref name="selected"/>, copied or shared as <paramref name="defensiveCopy"/> says.</summary>
    public SetSelectionModel(Func<TGridItem, KT> valueToKeyFn, ISet<KT> selected, bool defensiveCopy)
        : this(valueToKeyFn, selected, defensiveCopy, _ => true)
    {
    }

    /// <summary>Starts from <paramref name="selected"/>, copied or shared as <paramref name="defensiveCopy"/> says; the rows <paramref name="selectionEnabledFn"/> refuses keep their state.</summary>
    public SetSelectionModel(Func<TGridItem, KT> valueToKeyFn, ISet<KT> selected, bool defensiveCopy,
        Func<TGridItem, bool> selectionEnabledFn)
    {
        ValueToKeyFn = valueToKeyFn;
        this.selected = SelectedSet.Of(selected, defensiveCopy);
        this.selectionEnabledFn = selectionEnabledFn;
    }

    /// <summary>Records a change of the selection in <see cref="Version"/>.</summary>
    protected void IncrementVersion()
    {
        _version++;
    }

    /// <inheritdoc/>
    public int SelectedCount()
    {
        return selected.Count;
    }

    /// <summary>The keys of the items selected: the model's own set, not a copy; change the selection through the model.</summary>
    public IEnumerable<KT> Selected()
    {
        return selected;
    }

    /// <summary>Deselects every item, the locked ones too.</summary>
    public void Clear()
    {
        if (selected.Any())
        {
            selected.Clear();
            IncrementVersion();
        }
    }

    /// <inheritdoc/>
    public bool IsSelected(TGridItem item)
    {
        return selected.Contains(ValueToKeyFn.Invoke(item));
    }

    /// <inheritdoc/>
    public bool IsSelectionEnabled(TGridItem item)
    {
        return selectionEnabledFn(item);
    }

    /// <inheritdoc/>
    public void Select(TGridItem item, bool isSelected)
    {
        if (isSelected && !IsSelectionEnabled(item))
            return;   // a locked row keeps its state
        if (isSelected)
        {
            if (selected.Add(ValueToKeyFn.Invoke(item)))
                IncrementVersion();
        }
        else
        {
            if (selected.Remove(ValueToKeyFn.Invoke(item)))
                IncrementVersion();
        }
    }
}
internal static class SelectedSet
{
    // The copy keeps the comparer of the sets that expose one; any other set goes into a HashSet with the default one
    internal static ISet<T> Of<T>(ISet<T> set, bool defensiveCopy)
    {
        if (!defensiveCopy && !set.IsReadOnly)
            return set;
        return set switch
        {
            HashSet<T> hashSet => new HashSet<T>(hashSet, hashSet.Comparer),
            SortedSet<T> sortedSet => new SortedSet<T>(sortedSet, sortedSet.Comparer),
            ImmutableHashSet<T> immutableSet => new HashSet<T>(immutableSet, immutableSet.KeyComparer),
            ImmutableSortedSet<T> immutableSortedSet => new SortedSet<T>(immutableSortedSet, immutableSortedSet.KeyComparer),
            FrozenSet<T> frozenSet => new HashSet<T>(frozenSet, frozenSet.Comparer),
            _ => new HashSet<T>(set),
        };
    }
}

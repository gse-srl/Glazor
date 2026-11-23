namespace Glazor.Grds.Mdls;

/// <summary>
/// Selection model that supports "select all" functionality by maintaining a set of all possible
/// values and allowing the selection to be toggled for all items at once.
/// Select-all and deselect-all change only the candidates, the values the selection function accepts: any other value keeps
/// its state.
/// <see cref="AllSelected"/> is cached on the model's own changes: a change made to the selected set directly leaves it
/// stale.
/// </summary>
public class SetSelectionModelWithSelectAll<TGridItem> : SetSelectionModel<TGridItem>, ISelectionModelWithSelectAll<TGridItem>
{
    /// <summary>The candidates select-all selects: the values given that the selection function accepts.</summary>
    protected readonly HashSet<TGridItem> allPossibleValues;

    private int _cacheVersion = int.MinValue;
    private bool _cachedAllSelected;

    /// <summary>
    /// Most common case, the selection set is owned and so no defensive copy is needed.
    /// </summary>
    /// <param name="allPossibleValues">The candidates select-all selects.</param>
    public SetSelectionModelWithSelectAll(IEnumerable<TGridItem> allPossibleValues)
        : this(allPossibleValues, new HashSet<TGridItem>(), defensiveCopy: false)
    {
    }

    /// <summary>Starts from <paramref name="selected"/>, copied or shared as <paramref name="defensiveCopy"/> says.</summary>
    public SetSelectionModelWithSelectAll(IEnumerable<TGridItem> allPossibleValues, ISet<TGridItem> selected, bool defensiveCopy)
        : this(allPossibleValues, selected, defensiveCopy, _ => true)
    {
    }

    /// <summary>Starts from <paramref name="selected"/>, copied or shared as <paramref name="defensiveCopy"/> says; the rows <paramref name="selectionEnabledFn"/> refuses keep their state.</summary>
    public SetSelectionModelWithSelectAll(IEnumerable<TGridItem> allPossibleValues, ISet<TGridItem> selected, bool defensiveCopy,
        Func<TGridItem, bool> selectionEnabledFn)
        : base(selected, defensiveCopy, selectionEnabledFn)
    {
        this.allPossibleValues = allPossibleValues.Where(selectionEnabledFn).ToHashSet();
    }

    /// <inheritdoc/>
    public bool AllSelected()
    {
        if (_cacheVersion != Version)
        {
            _cachedAllSelected = allPossibleValues.Count > 0 && selected.IsSupersetOf(allPossibleValues);   // the selected set's comparer decides
            _cacheVersion = Version;
        }
        return _cachedAllSelected;
    }

    /// <inheritdoc/>
    public void DeselectAll()
    {
        int count = selected.Count;
        selected.ExceptWith(allPossibleValues);
        if (selected.Count != count)
            IncrementVersion();
    }

    /// <inheritdoc/>
    public void SelectAll()
    {
        int count = selected.Count;
        selected.UnionWith(allPossibleValues);
        if (selected.Count != count)
            IncrementVersion();
    }
}

/// <summary>
/// Provides a selection model that supports selecting and deselecting all items, in addition to individual selection,
/// for a set of grid items.
/// <see cref="AllSelected"/> is cached on the model's own changes: a change made to the selected set directly leaves it
/// stale.
/// </summary>
/// <remarks>This selection model enables bulk selection operations, such as selecting or deselecting all items
/// that are eligible for selection. It is useful in scenarios where users need to quickly select or clear all items in
/// a data grid or similar UI component. The model respects selection eligibility as determined by the provided
/// selection predicate: select-all and deselect-all change only the candidates, and any other key keeps its state.
/// When <typeparamref name="TGridItem"/> and <typeparamref name="KT"/> are the same type, the constructors taking rows and
/// those taking keys match the same call: name the first argument, <c>allPossibleValues:</c> or <c>allPossibleKeys:</c>.</remarks>
/// <typeparam name="TGridItem">The type of items contained in the grid.</typeparam>
/// <typeparam name="KT">The type of the key used to uniquely identify each grid item.</typeparam>
public class SetSelectionModelWithSelectAll<TGridItem, KT> : SetSelectionModel<TGridItem, KT>, ISelectionModelWithSelectAll<TGridItem>
{
    /// <summary>The keys of the candidates select-all selects.</summary>
    protected readonly HashSet<KT> allPossibleKeys;

    private int _cacheVersion = int.MinValue;
    private bool _cachedAllSelected;

    /// <summary>
    /// Most common case, the selection set is owned and so no defensive copy is needed.
    /// </summary>
    /// <param name="allPossibleValues">The candidates select-all selects, by their keys.</param>
    /// <param name="valueToKeyFn">The key of an item: the selection holds keys.</param>
    public SetSelectionModelWithSelectAll(IEnumerable<TGridItem> allPossibleValues, Func<TGridItem, KT> valueToKeyFn)
        : this(allPossibleValues, valueToKeyFn, new HashSet<KT>(), defensiveCopy: false)
    {
    }

    /// <summary>
    /// Most common case, the selection set is owned and so no defensive copy is needed.
    /// </summary>
    public SetSelectionModelWithSelectAll(IEnumerable<KT> allPossibleKeys, Func<TGridItem, KT> valueToKeyFn)
        : this(allPossibleKeys, valueToKeyFn, new HashSet<KT>(), defensiveCopy: false)
    {
    }

    /// <summary>Starts from <paramref name="selected"/>, copied or shared as <paramref name="defensiveCopy"/> says.</summary>
    public SetSelectionModelWithSelectAll(IEnumerable<TGridItem> allPossibleValues, Func<TGridItem, KT> valueToKeyFn, ISet<KT> selected,
        bool defensiveCopy)
        : this(allPossibleValues, valueToKeyFn, selected, defensiveCopy, _ => true)
    {
    }

    /// <summary>Starts from <paramref name="selected"/>, copied or shared as <paramref name="defensiveCopy"/> says.</summary>
    public SetSelectionModelWithSelectAll(IEnumerable<KT> allPossibleKeys, Func<TGridItem, KT> valueToKeyFn, ISet<KT> selected,
        bool defensiveCopy)
        : this(allPossibleKeys, valueToKeyFn, selected, defensiveCopy, _ => true)
    {
    }

    /// <summary>Starts from <paramref name="selected"/>, copied or shared as <paramref name="defensiveCopy"/> says; the rows <paramref name="selectionEnabledFn"/> refuses keep their state.</summary>
    public SetSelectionModelWithSelectAll(IEnumerable<TGridItem> allPossibleValues, Func<TGridItem, KT> valueToKeyFn, 
        ISet<KT> selected, bool defensiveCopy, Func<TGridItem, bool> selectionEnabledFn)
        : base(valueToKeyFn, selected, defensiveCopy, selectionEnabledFn)
    {
        this.allPossibleKeys = allPossibleValues.Where(selectionEnabledFn).Select(valueToKeyFn).ToHashSet();
    }

    /// <summary>
    /// The keys are the candidates as given: leave out those of the rows <paramref name="selectionEnabledFn"/> refuses, or
    /// select-all selects them.
    /// </summary>
    public SetSelectionModelWithSelectAll(IEnumerable<KT> allPossibleKeys, Func<TGridItem, KT> valueToKeyFn,
        ISet<KT> selected, bool defensiveCopy, Func<TGridItem, bool> selectionEnabledFn)
        : base(valueToKeyFn, selected, defensiveCopy, selectionEnabledFn)
    {
        this.allPossibleKeys = allPossibleKeys.ToHashSet();
    }

    /// <inheritdoc/>
    public bool AllSelected()
    {
        if (_cacheVersion != Version)
        {
            _cachedAllSelected = allPossibleKeys.Count > 0 && selected.IsSupersetOf(allPossibleKeys);
            _cacheVersion = Version;
        }
        return _cachedAllSelected;
    }

    /// <inheritdoc/>
    public void DeselectAll()
    {
        int count = selected.Count;
        selected.ExceptWith(allPossibleKeys);
        if (selected.Count != count)
            IncrementVersion();
    }

    /// <inheritdoc/>
    public void SelectAll()
    {
        int count = selected.Count;
        selected.UnionWith(allPossibleKeys);
        if (selected.Count != count)
            IncrementVersion();
    }
}

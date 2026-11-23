namespace Glazor.Grds.Mdls;


/// <summary>
/// Represents a selection model that allows at most one item to be selected at a time.
/// </summary>
/// <remarks>This selection model is suitable for scenarios where only a single item can be selected, such as
/// radio button groups or single-row selection in data grids. Selection can be restricted to specific items by
/// providing a predicate to the constructor.</remarks>
/// <typeparam name="TGridItem">The type of items managed by the selection model.</typeparam>
public class SingleSelectionModel<TGridItem> : ISelectionModel<TGridItem>
{
    private bool hasSelection;   // I'm using it to handle value types too
    private TGridItem? selected;
    private readonly Func<TGridItem, bool> selectionEnabledFn;

    /// <summary>A model where every item can be selected.</summary>
    public SingleSelectionModel() : this(_ => true)
    {
    }

    /// <param name="selectionEnabledFn">Whether an item can be selected: selecting one it refuses does nothing.</param>
    public SingleSelectionModel(Func<TGridItem, bool> selectionEnabledFn)
    {
        this.selectionEnabledFn = selectionEnabledFn;
    }

    /// <inheritdoc/>
    public int SelectedCount()
    {
        return hasSelection ? 1 : 0;
    }

    /// <summary>The item selected, or none.</summary>
    public IEnumerable<TGridItem> Selected()
    {
        return hasSelection ? new[] { selected! } : [];
    }

    /// <summary>Deselects the item selected, if any.</summary>
    public void Clear()
    {
        hasSelection = false;
        selected = default;
    }

    /// <inheritdoc/>
    public bool IsSelected(TGridItem item)
    {
        return hasSelection && EqualityComparer<TGridItem>.Default.Equals(selected, item);
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
            hasSelection = true;
            selected = item;
        }
        else if (IsSelected(item))
        {
            Clear();
        }
    }
}

/// <summary>
/// A selection model that allows at most one item to be selected at a time, held by its key.
/// </summary>
/// <typeparam name="TGridItem">The type of items managed by the selection model.</typeparam>
/// <typeparam name="KT">The type of an item's key.</typeparam>
public class SingleSelectionModel<TGridItem, KT> : ISelectionModel<TGridItem>
{
    private bool hasSelection;
    private KT? selected;
    private readonly Func<TGridItem, bool> selectionEnabledFn;

    /// <summary>The key of an item: the selection holds keys.</summary>
    public Func<TGridItem, KT> ValueToKeyFn { get; }

    /// <summary>A model where every item can be selected.</summary>
    /// <param name="valueToKeyFn">The key of an item: the selection holds keys.</param>
    public SingleSelectionModel(Func<TGridItem, KT> valueToKeyFn) : this(valueToKeyFn, _ => true)
    {
    }

    /// <param name="valueToKeyFn">The key of an item: the selection holds keys.</param>
    /// <param name="selectionEnabledFn">Whether an item can be selected: selecting one it refuses does nothing.</param>
    public SingleSelectionModel(Func<TGridItem, KT> valueToKeyFn, Func<TGridItem, bool> selectionEnabledFn)
    {
        ValueToKeyFn = valueToKeyFn;
        this.selectionEnabledFn = selectionEnabledFn;
    }

    /// <inheritdoc/>
    public int SelectedCount()
    {
        return hasSelection ? 1 : 0;
    }

    /// <summary>The key of the item selected, or none.</summary>
    public IEnumerable<KT> Selected()
    {
        return hasSelection ? new[] { selected! } : [];
    }

    /// <summary>Deselects the item selected, if any.</summary>
    public void Clear()
    {
        hasSelection = false;
        selected = default;
    }

    /// <inheritdoc/>
    public bool IsSelected(TGridItem item)
    {
        return hasSelection && EqualityComparer<KT>.Default.Equals(selected, ValueToKeyFn.Invoke(item));
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
            hasSelection = true;
            selected = ValueToKeyFn.Invoke(item);
        }
        else if (IsSelected(item))
        {
            Clear();
        }
    }
}

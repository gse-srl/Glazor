using Glazor.Grds.Mdls;
using Glazor.Misc;
using Microsoft.EntityFrameworkCore;

namespace Glazor.EntityFrameworkCore;

/// <summary>
/// Provides an editable data grid model for Entity Framework-backed collections, supporting row editing and insertion
/// operations.
/// </summary>
/// <remarks>This class extends the base EF data grid model to enable editing and inserting rows within the grid.
/// It manages edit and insert states, ensuring that only one edit or insert operation is pending at a time. Use this
/// model when you need to allow users to modify or add items directly in a data grid bound to an Entity Framework data
/// source.</remarks>
/// <typeparam name="TDbContext">The EF Core context type the model queries through <c>DbContextFactory</c>.</typeparam>
/// <typeparam name="TGridItem">The type of items displayed and managed in the data grid. Must be a reference type.</typeparam>
public class EditableEFDataGridModel<TDbContext, TGridItem> : EFDataGridModel<TDbContext, TGridItem>, IEditableDataGridModel<TGridItem> 
    where TDbContext : DbContext where TGridItem : class
{
    private readonly EditSupport<TGridItem> _editSupport;

    // Rows shown although the filter excludes them, by key, until the next query. True when the database still counts the row
    // among those read, because only a filter applied in memory excludes it
    private readonly Dictionary<object, bool> _pinned = new();

    // The form changes the item itself, so what the reload compares with is taken when the edit starts
    private (TGridItem Item, object Key, object?[]? Order)? _editStart;

    /// <summary>A model with no filter and nothing being edited; set up as <see cref="EFDataGridModel{TDbContext, TGridItem}"/> is.</summary>
    public EditableEFDataGridModel()
    {
        _editSupport = new EditSupport<TDbContext, TGridItem>(this, InsertPostedAsync);
    }

    /// <inheritdoc/>
    public async ValueTask CancelAsync(CancellationToken ct = default)
    {
        await _editSupport.CancelAsync(ct);
    }

    /// <inheritdoc/>
    public async ValueTask<TGridItem> EditAsync()
    {
        if (!IsCurrentRowEditable())
            throw new InvalidOperationException("The current row is not editable");
        // Read before the edit starts: a base query that throws here leaves nothing pending
        object?[]? order = GetCurrentItem() is { } current ? await PagingOrderValuesAsync(current) : null;
        TGridItem item = await _editSupport.EditAsync();
        _editStart = (item, GetItemKeyFn().Invoke(item), order);
        return item;
    }

    /// <inheritdoc/>
    public override void ForceReload(bool restoreCurrentRow)
    {
        base.ForceReload(restoreCurrentRow);
        _pinned.Clear();
    }

    private protected override bool ShownDespiteTheFilter(TGridItem item) => _pinned.Count > 0 && _pinned.ContainsKey(GetItemKeyFn().Invoke(item));

    /// <summary>
    /// A row the filter no longer admits stays shown where it was, pinned until the next query. A new query puts in its place a
    /// row whose value in the order the database pages by changed, and a pinned one the filter the database applies admits again.
    /// </summary>
    protected internal override async ValueTask<bool> ReloadItemAsync(int rowIndex, object pk, CancellationToken ct = default)
    {
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        TGridItem item = GetItem(rowIndex);
        int reloads = ReloadsSoFar();
        // by key too: a row read back from disk during the edit is a copy of the edited item
        (TGridItem Item, object Key, object?[]? Order) start = _editStart is { } editStart
                && (ReferenceEquals(editStart.Item, item) || Equals(editStart.Key, GetItemKeyFn().Invoke(item)))
            ? editStart : (item, GetItemKeyFn().Invoke(item), await PagingOrderValuesAsync(item, ct));
        _editStart = null;
        bool counted = !_pinned.TryGetValue(start.Key, out bool pinnedInTheQuery) || pinnedInTheQuery;   // among the rows the offset counts
        TGridItem? nitem = await QueryItemAsync(item, pk, throughTheFilter: true, ct);
        _pinned.Remove(start.Key);   // after the query: one that fails leaves the row as the offset counts it
        ThrowIfReloadOvertaken(reloads);
        if (nitem != null)
        {
            if (!counted || MovedInTheOrder(start.Order, await PagingOrderValuesAsync(nitem, ct)))
            {
                ForceReload(true);
                if (!PassesTheMemoryFilters(nitem))
                    _pinned[GetItemKeyFn().Invoke(nitem)] = true;
                return true;
            }
            ThrowIfReloadOvertaken(reloads);
            Set(rowIndex, nitem);
            if (!PassesTheMemoryFilters(nitem))
                _pinned[GetItemKeyFn().Invoke(nitem)] = true;
            return true;
        }
        // Deleted, or out of the filter the database applies: no longer among the rows read either way
        if (counted)
            LeftTheRowsRead();
        _pinned[start.Key] = false;   // no longer counted, also if the query below fails
        TGridItem? shown = FiltersInTheDatabase() ? await QueryItemAsync(item, pk, throughTheFilter: false, ct) : default;
        ThrowIfReloadOvertaken(reloads);
        if (shown == null)
        {
            Remove(rowIndex);
            return false;
        }
        Set(rowIndex, shown);
        _pinned[GetItemKeyFn().Invoke(shown)] = false;
        return true;
    }

    // A new row the filter the database applies excludes stays where it was inserted; any other takes its place in a new query
    private async ValueTask InsertPostedAsync(TGridItem newItem, CancellationToken ct)
    {
        object key = GetItemKeyFn().Invoke(newItem);
        if (FiltersInTheDatabase())
        {
            if (await QueryItemAsync(newItem, key, throughTheFilter: true, ct) == null)
            {
                TGridItem? shown = await QueryItemAsync(newItem, key, throughTheFilter: false, ct);
                int rowIndex = RowIndexOf(newItem);
                if (shown != null && rowIndex >= 0)
                {
                    Set(rowIndex, shown);
                    _pinned[GetItemKeyFn().Invoke(shown)] = false;   // never among the rows read
                    return;
                }
            }
        }
        ForceReload(true);
        _pinned[key] = true;   // a filter applied in memory may read what RefineItemAsync supplies, which the posted item lacks
    }

    /// <inheritdoc/>
    public async ValueTask<TGridItem> InsertAsync(TGridItem newItemProto, bool afterCurrentRow)
    {
        return await _editSupport.InsertAsync(newItemProto, afterCurrentRow);
    }

    /// <inheritdoc/>
    public async ValueTask PostAsync(CancellationToken ct = default)
    {
        await _editSupport.PostAsync(ct);
    }

    /// <inheritdoc/>
    public virtual bool IsEditPending()
    {
        return _editSupport.IsEditPending();
    }

    /// <inheritdoc/>
    public virtual bool IsInsertPending()
    {
        return _editSupport.IsInsertPending();
    }

    // The row being inserted is not in the list: EditSupport maps the rows the grid sees to the rows the list holds
    /// <inheritdoc/>
    protected override int GetCurrentRowCountNoSync() => _editSupport.CountWithPending(base.GetCurrentRowCountNoSync());

    /// <inheritdoc/>
    public override TGridItem GetItem(int rowIndex)
        => _editSupport.IsPendingRow(rowIndex) ? _editSupport.InsertingItem().RequireNotNull() : base.GetItem(_editSupport.BackingIndex(rowIndex));

    /// <inheritdoc/>
    protected override int GetRowCountNoSync() => _editSupport.CountWithPending(base.GetRowCountNoSync());

    /// <inheritdoc/>
    public override async ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
    {
        if (!IsInsertPending() || count < 0)
            return await base.FetchRowsAsync(rowIndex, count, ct);
        // The pending row is not in the backing list: ask for one row less when the window covers it, and count it in the answer
        int insertRow = _editSupport.InsertRow();
        bool covered = rowIndex <= insertRow && insertRow < rowIndex + count;
        int baseIndex = rowIndex > insertRow ? rowIndex - 1 : rowIndex;
        int baseCount = covered ? count - 1 : count;
        int fetched = baseCount > 0 ? await base.FetchRowsAsync(baseIndex, baseCount, ct) : 0;
        return fetched + (covered ? 1 : 0);
    }

    /// <inheritdoc/>
    public virtual bool IsCurrentRowEditable()
    {
        return true;
    }

    /// <inheritdoc/>
    public override bool IsCurrentRowFrozen()
    {
        return IsEditPending() || IsInsertPending() || base.IsCurrentRowFrozen();
    }

    /// <inheritdoc/>
    public override int RowIndexOf(TGridItem item) => _editSupport.IsPendingItem(item) ? _editSupport.InsertRow() : base.RowIndexOf(item);

    /// <inheritdoc/>
    public override int GetCurrentRow() => _editSupport.CurrentRow(base.GetCurrentRow());

    /// <inheritdoc/>
    public override void SetCurrentRow(int rowIndex)
    {
        if (IsCurrentRowFrozen() && rowIndex != GetCurrentRow())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        if (!IsInsertPending()) 
            base.SetCurrentRow(rowIndex);
    }
}

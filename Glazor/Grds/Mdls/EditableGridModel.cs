using Glazor.Misc;

namespace Glazor.Grds.Mdls;

/// <summary>
/// A <see cref="GridModel{TGridItem}"/> with the edit lifecycle of <see cref="EditableDataGridModel{TGridItem}"/>.
/// </summary>
public class EditableGridModel<TGridItem> : GridModel<TGridItem>, IEditableGridModel<TGridItem> where TGridItem : class
{
    private readonly EditSupport<TGridItem> _editSupport;

    /// <summary>A model that reads back only the row a post or a cancel touched, when its key did not change.</summary>
    /// <param name="reloadAllFn">Reads every row again from the store.</param>
    /// <param name="reloadItemFn">Reads again the row the item names, by its key; null when the row is gone, which takes it out of the list.</param>
    public EditableGridModel(Func<ValueTask<IList<TGridItem>>> reloadAllFn,
        Func<TGridItem, ValueTask<TGridItem?>> reloadItemFn)
    {
        _editSupport = new(this, reloadAllFn, reloadItemFn);
    }

    /// <summary>A model that reads every row back after a post or a cancel.</summary>
    /// <param name="reloadAllFn">Reads every row again from the store.</param>
    public EditableGridModel(Func<ValueTask<IList<TGridItem>>> reloadAllFn)
    {
        _editSupport = new(this, reloadAllFn);
    }

    /// <inheritdoc/>
    public async ValueTask CancelAsync(CancellationToken ct = default) => await _editSupport.CancelAsync(ct);

    /// <inheritdoc/>
    public async ValueTask<TGridItem> EditAsync()
    {
        if (!IsCurrentRowEditable())
            throw new InvalidOperationException("The current row is not editable");
        return await _editSupport.EditAsync();
    }

    /// <inheritdoc/>
    public async ValueTask<TGridItem> InsertAsync(TGridItem newItemProto, bool afterCurrentRow) => await _editSupport.InsertAsync(newItemProto, afterCurrentRow);

    /// <inheritdoc/>
    public async ValueTask PostAsync(CancellationToken ct = default) => await _editSupport.PostAsync(ct);

    /// <inheritdoc/>
    public virtual bool IsEditPending() => _editSupport.IsEditPending();

    /// <inheritdoc/>
    public virtual bool IsInsertPending() => _editSupport.IsInsertPending();

    /// <inheritdoc/>
    public virtual bool IsCurrentRowEditable() => true;

    // The row being inserted is not in the list: EditSupport maps the rows the grid sees to the rows the list holds
    /// <inheritdoc/>
    public override int GetCurrentRowCount() => _editSupport.CountWithPending(base.GetCurrentRowCount());

    /// <inheritdoc/>
    public override int GetRowCount() => _editSupport.CountWithPending(base.GetRowCount());

    /// <inheritdoc/>
    public override TGridItem GetItem(int rowIndex)
        => _editSupport.IsPendingRow(rowIndex) ? _editSupport.InsertingItem().RequireNotNull() : base.GetItem(_editSupport.BackingIndex(rowIndex));

    /// <inheritdoc/>
    public override int RowIndexOf(TGridItem item) => _editSupport.IsPendingItem(item) ? _editSupport.InsertRow() : base.RowIndexOf(item);

    /// <inheritdoc/>
    public override int GetCurrentRow() => _editSupport.CurrentRow(base.GetCurrentRow());

    /// <inheritdoc/>
    public override bool IsCurrentRowFrozen() => IsEditPending() || IsInsertPending() || base.IsCurrentRowFrozen();

    /// <inheritdoc/>
    public override void SetCurrentRow(int rowIndex)
    {
        if (IsCurrentRowFrozen() && rowIndex != GetCurrentRow())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        if (!IsInsertPending())
            base.SetCurrentRow(rowIndex);
    }
}

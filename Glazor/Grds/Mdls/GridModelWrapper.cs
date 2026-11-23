using Microsoft.AspNetCore.Components;

namespace Glazor.Grds.Mdls;

/// <summary>
/// A model that forwards every member to the one it wraps, to derive from and override the members to change. Column
/// registration is forwarded too, so a wrapper needs no access to internals. <see cref="DataGridModelWrapper{TGridItem}"/> and
/// <see cref="TreeGridModelWrapper{TGridItem}"/> do the same for the richer interfaces.<br/>
/// The members built on others are forwarded too, as the wrapped model answers them best: a subclass that changes
/// <see cref="GetItem"/>, <see cref="GetValue"/> or <see cref="GetCurrentRow"/> overrides <see cref="GetValueAt"/>,
/// <see cref="GetCurrentItem"/> and, on <see cref="TreeGridModelWrapper{TGridItem}"/>, <c>GetCurrentTreeNode</c> as well.
/// </summary>
public class GridModelWrapper<TGridItem> : IGridModel<TGridItem>
{
    /// <summary>The model wrapped.</summary>
    protected internal readonly IGridModel<TGridItem> _gridModel;

    /// <summary>Wraps <paramref name="wrapped"/>, forwarding every member to it.</summary>
    protected GridModelWrapper(IGridModel<TGridItem> wrapped)
    {
        _gridModel = wrapped ?? throw new ArgumentNullException(nameof(wrapped));
    }

    // The handlers are kept here as well, for FireModelChanged; the wrapped model holds them for its own changes
    private OnModelChangedEventHandler? _onModelChanged;

    /// <inheritdoc/>
    public event OnModelChangedEventHandler? OnModelChanged
    {
        add { _gridModel.OnModelChanged += value; _onModelChanged += value; }
        remove { _gridModel.OnModelChanged -= value; _onModelChanged -= value; }
    }

    /// <summary>
    /// Raises <see cref="OnModelChanged"/> for a change of the wrapper's own, which the wrapped model does not see: the rows a
    /// wrapper that filters them shows, for one.
    /// </summary>
    protected void FireModelChanged(GridModelChangeType changeType) => _onModelChanged?.Invoke(changeType);

    /// <inheritdoc/>
    public virtual void EnrichColumn<T>(AGrid<TGridItem> grid, EnrichGridColumn<TGridItem, T> gridColumn)
        => _gridModel.EnrichColumn(grid, gridColumn);

    /// <inheritdoc/>
    public virtual void SetColumn<T>(AGrid<TGridItem> grid, GridColumn<TGridItem, T> gridColumn)
        => _gridModel.SetColumn(grid, gridColumn);

    /// <inheritdoc/>
    public virtual void UnsetColumn(AGrid<TGridItem> grid, string columnName)
        => _gridModel.UnsetColumn(grid, columnName);

    /// <inheritdoc/>
    public virtual bool RequiresFetch() => _gridModel.RequiresFetch();

    /// <inheritdoc/>
    public virtual async ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default)
        => await _gridModel.FetchRowsAsync(rowIndex, count, ct);

    /// <inheritdoc/>
    public virtual IColumn<TGridItem> GetColumn(int columnIndex)
        => _gridModel.GetColumn(columnIndex);

    /// <inheritdoc/>
    public virtual IColumnInfo<TGridItem> GetColumnInfo(int columnIndex)
        => _gridModel.GetColumnInfo(columnIndex);

    /// <inheritdoc/>
    public virtual int GetColumnCount()
        => _gridModel.GetColumnCount();

    /// <inheritdoc/>
    public virtual int GetColumnsVersion()
        => _gridModel.GetColumnsVersion();

    /// <inheritdoc/>
    public virtual int GetColumnIndex(string columnName)
        => _gridModel.GetColumnIndex(columnName);

    /// <inheritdoc/>
    public virtual string GetColumnName(int columnIndex)
        => _gridModel.GetColumnName(columnIndex);

    /// <inheritdoc/>
    public virtual TGridItem? GetCurrentItem()
        => _gridModel.GetCurrentItem();

    /// <inheritdoc/>
    public virtual void SetCurrentItem(TGridItem item)
        => _gridModel.SetCurrentItem(item);

    /// <inheritdoc/>
    public virtual int GetCurrentRow()
        => _gridModel.GetCurrentRow();

    /// <inheritdoc/>
    public virtual int GetCurrentRowCount()
        => _gridModel.GetCurrentRowCount();

    /// <inheritdoc/>
    public virtual int GetDataVersion()
        => _gridModel.GetDataVersion();

    /// <inheritdoc/>
    public virtual IReadOnlyList<TGridItem> GetDataView()
        => _gridModel.GetDataView();

    /// <inheritdoc/>
    public virtual TGridItem GetItem(int rowIndex)
        => _gridModel.GetItem(rowIndex);

    /// <inheritdoc/>
    public virtual Type GetItemType()
        => _gridModel.GetItemType();

    /// <inheritdoc/>
    public virtual int GetRowCount()
        => _gridModel.GetRowCount();

    /// <inheritdoc/>
    public virtual object? GetValue(TGridItem item, int columnIndex)
        => _gridModel.GetValue(item, columnIndex);

    /// <inheritdoc/>
    public virtual object? GetValueAt(int rowIndex, int columnIndex)
        => _gridModel.GetValueAt(rowIndex, columnIndex);

    /// <inheritdoc/>
    public virtual bool IsCurrentRowFrozen()
        => _gridModel.IsCurrentRowFrozen();

    /// <inheritdoc/>
    public virtual int RowIndexOf(TGridItem item)
        => _gridModel.RowIndexOf(item);

    /// <inheritdoc/>
    public virtual bool SameItem(TGridItem? item1, TGridItem? item2)
        => _gridModel.SameItem(item1, item2);

    /// <inheritdoc/>
    public virtual void SetCurrentRow(int rowIndex)
        => _gridModel.SetCurrentRow(rowIndex);

    /// <inheritdoc/>
    public virtual void Sync()
        => _gridModel.Sync();

    /// <inheritdoc/>
    public virtual Func<TGridItem, object>? GetItemKeyFn()
        => _gridModel.GetItemKeyFn();
}

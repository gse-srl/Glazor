using Glazor.Grds.Filters;

namespace Glazor.Grds.Mdls;

/// <summary>A <see cref="GridModelWrapper{TGridItem}"/> that also forwards filtering, sorting and the searches of a data model.</summary>
public class DataGridModelWrapper<TGridItem> : GridModelWrapper<TGridItem>, IDataGridModel<TGridItem>
{
    /// <summary>The model wrapped.</summary>
    protected internal readonly IDataGridModel<TGridItem> _dataGridModel;

    /// <summary>Wraps <paramref name="wrapped"/>, forwarding every member to it.</summary>
    protected DataGridModelWrapper(IDataGridModel<TGridItem> wrapped) : base(wrapped)
    {
        _dataGridModel = wrapped ?? throw new ArgumentNullException(nameof(wrapped));
    }

    /// <inheritdoc/>
    public virtual void ApplyFilters(IEnumerable<KeyValuePair<string, AFilterDef>> filters)
        => _dataGridModel.ApplyFilters(filters);

    /// <inheritdoc/>
    public virtual bool CanDoContainsSearch() => _dataGridModel.CanDoContainsSearch();

    /// <inheritdoc/>
    public virtual bool CanSearchOnSortedColumn() => _dataGridModel.CanSearchOnSortedColumn();

    /// <inheritdoc/>
    public virtual async ValueTask<int> ContainsSearchAsync(string columnName, string value, bool forward, int startFrom, CancellationToken ct = default)
        => await _dataGridModel.ContainsSearchAsync(columnName, value, forward, startFrom, ct);

    /// <inheritdoc/>
    public virtual IReadOnlyDictionary<string, AFilterDef> GetFilters() => _dataGridModel.GetFilters();

    /// <inheritdoc/>
    public virtual string? GetSortedColumn() => _dataGridModel.GetSortedColumn();

    /// <inheritdoc/>
    public virtual SortType GetSortType() => _dataGridModel.GetSortType();

    /// <inheritdoc/>
    public virtual bool IsFilterable() => _dataGridModel.IsFilterable();

    /// <inheritdoc/>
    public virtual bool IsFiltered() => _dataGridModel.IsFiltered();

    /// <inheritdoc/>
    public virtual bool IsSortable() => _dataGridModel.IsSortable();

    /// <inheritdoc/>
    public virtual async ValueTask<int> SearchOnSortedColumnAsync(object? value, CancellationToken ct = default) => await _dataGridModel.SearchOnSortedColumnAsync(value, ct);

    /// <inheritdoc/>
    public virtual void SetSortedColumn(string? columnName, bool ascending) => _dataGridModel.SetSortedColumn(columnName, ascending);
}

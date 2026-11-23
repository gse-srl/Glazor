using Glazor.Grds.Filters;

namespace Glazor.Grds.Mdls;

/// <summary>
/// Specialized <see cref="IGridModel{TGridItem}"/> to be used with <see cref="Grigliaria{TGridItem}"/>.
/// Adds support for sorting and filtering.
/// </summary>
public interface IDataGridModel<TGridItem> : IGridModel<TGridItem>
{
    /// <summary>
    /// Indicates whether the model supports changing the sort order.
    /// </summary>
    /// <returns>True if the model is sortable; otherwise, false.</returns>
    bool IsSortable();

    /// <summary>
    /// Changes the sort order if the model is sortable.
    /// </summary>
    /// <param name="columnName">The name of the column to sort.</param>
    /// <param name="ascending">True for ascending order; false for descending.</param>
    void SetSortedColumn(string? columnName, bool ascending);

    /// <summary>
    /// Gets the sorted column name, or null if none.
    /// </summary>
    /// <returns>The sorted column name.</returns>
    string? GetSortedColumn();

    /// <summary>
    /// Indicates whether the sorted order is ascending, descending, or not sorted.
    /// </summary>
    SortType GetSortType();

    /// <summary>
    /// Searches for the given value in the currently sorted column.
    /// </summary>
    /// <param name="value">The value to search for.</param>
    /// <param name="ct">Observed by models that fetch while searching; an in-memory model has nothing to cancel.</param>
    /// <returns>A result compatible with binary search.</returns>
    /// <exception cref="InvalidOperationException">Thrown if there isn't a sorted column, or the current row is frozen.</exception>
    ValueTask<int> SearchOnSortedColumnAsync(object? value, CancellationToken ct = default);

    /// <summary>
    /// Indicates whether <see cref="SearchOnSortedColumnAsync(object, CancellationToken)"/> can be used.
    /// </summary>
    /// <returns>True if the method can be used; otherwise, false.</returns>
    bool CanSearchOnSortedColumn();

    /// <summary>
    /// Finds the next row, or the previous one, whose value in a column contains a text, ignoring case: what a host's own
    /// "Find" box calls, from the row after the current one. No grid calls it. A value that is not a string is matched on its
    /// text; a paged model reads on as far as the search needs. Without an implementation of its own, a model does not support it.
    /// </summary>
    /// <param name="columnName">The name of the column to search.</param>
    /// <param name="value">The text to find.</param>
    /// <param name="forward">True to search toward the end; false toward the start.</param>
    /// <param name="startFrom">The row the search starts at, itself included; below 0 searching forward starts at the first row.</param>
    /// <param name="ct">Observed by models that fetch while searching; an in-memory model has nothing to cancel.</param>
    /// <returns>The row found, less than zero if none.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the current row is frozen.</exception>
    /// <exception cref="NotSupportedException">Thrown when <see cref="CanDoContainsSearch"/> is false.</exception>
    ValueTask<int> ContainsSearchAsync(string columnName, string value, bool forward, int startFrom, CancellationToken ct = default)
        => throw new NotSupportedException();

    /// <summary>
    /// Indicates whether <see cref="ContainsSearchAsync(string, string, bool, int, CancellationToken)"/> can be used.
    /// </summary>
    /// <returns>True if the method can be used; false by default.</returns>
    bool CanDoContainsSearch() => false;

    /// <summary>
    /// Indicates whether the model supports data filtering.
    /// </summary>
    /// <returns>True if the model is filterable; otherwise, false.</returns>
    bool IsFilterable();

    /// <summary>
    /// Applies data filters if the model is filterable.
    /// </summary>
    /// <param name="filters">The filters to apply, per column.</param>
    void ApplyFilters(IEnumerable<KeyValuePair<string, AFilterDef>>filters);

    /// <summary>
    /// Gets the applied filters as an unmodifiable map, empty if no filters are applied. Never null. The map is a live view, which
    /// follows later changes; the grids' <c>GetFilters</c> returns a copy.
    /// </summary>
    /// <returns>The applied filters, keyed by column name.</returns>
    IReadOnlyDictionary<string, AFilterDef> GetFilters();

    /// <summary>Whether a filter that is not void is applied to any column.</summary>
    bool IsFiltered();
}

/// <summary>How a model is sorted, as <c>GetSortType</c> gives it: not at all, ascending or descending.</summary>
public enum SortType {
    /// <summary>No column is sorted.</summary>
    None = 0,
    /// <summary>The sorted column is ascending.</summary>
    Asc,
    /// <summary>The sorted column is descending.</summary>
    Desc,
}


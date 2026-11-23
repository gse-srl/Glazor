using Glazor.Grds.Filters;
using Glazor.Misc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Glazor.Grds.Mdls;

/// <summary>
/// An IDataGridModel backed by a List.
/// It supports in memory data filtering and sorting (for columns bound to a "comparable" item property).
/// </summary>
public class DataGridModel<TGridItem> : AMemGridModel<TGridItem>, IDataGridModel<TGridItem> where TGridItem : class
{
    private static readonly ILogger<DataGridModel<TGridItem>> logger = LoggerSupport.Logger<DataGridModel<TGridItem>>();

    private readonly MyList<TGridItem> _orgData;

    private string? _sortedColumn;
    private bool _sortedAsc;

    private readonly FiltersDefs _filtersDefs;

    private TGridItem? _itemToBeRestored;
    private TGridItem? _previous;   // the current row before the first change since the sync: an unsorted view is the live list
    private int _currentRow;

    private bool _inSync;
    private int _version;

    private ReadOnlyCollection<TGridItem>? _data;

    /// <summary>
    /// Gives a row's key, which finds the row again after a reload, a sort or a filter change; without it rows are compared
    /// with <c>Equals</c>. An editable model requires it.
    /// </summary>
    public Func<TGridItem, object>? ItemKeyFn { get; init; }

    /// <summary>An empty model; fill it through <c>GetData()</c>.</summary>
    public DataGridModel() : this([])
    {
    }

    /// <summary>A model over a copy of <paramref name="data"/>; change the rows later through <c>GetData()</c>.</summary>
    public DataGridModel(IEnumerable<TGridItem> data)
    {
        _filtersDefs = new();
        _orgData = new MyList<TGridItem>(this, new List<TGridItem>(data));
    }

    /// <inheritdoc/>
    public sealed override Func<TGridItem, object>? GetItemKeyFn() => ItemKeyFn;

    /// <summary>Takes the column out of the model, with its filter and its sort.</summary>
    /// <returns>The column removed, or null when the model has no such column.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the current row is frozen and the column is sorted or filtered.</exception>
    public override IColumn<TGridItem>? RemoveColumn(string columnName)
    {
        if (IsCurrentRowFrozen() && (columnName == _sortedColumn || _filtersDefs.Definitions.ContainsKey(columnName)))
            // dropping its sort or filter would move the rows around the pending one
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        IColumn<TGridItem>? res = base.RemoveColumn(columnName);
        if (res == null)
            return null;
        if (columnName == _sortedColumn)
        {
            _sortedColumn = null;
            Invalidate(default);
        }
        if (_filtersDefs.Remove(columnName))
        {
            Invalidate(default);
        }
        return res;
    }

    /// <summary>
    /// Forces filters and sorting to be applied again at next sync.
    /// </summary>
    /// <returns>true if it was in sync</returns>
    protected bool Invalidate() => Invalidate(default);

    /// <summary>
    /// Forces filters and sorting to be applied again at next sync.
    /// </summary>
    /// <param name="goTo">The row to make current after the sync; default keeps the current one.</param>
    /// <param name="explicitGoto">True when <paramref name="goTo"/> comes from <c>SetAndGoto</c>, which holds before the first sync too.</param>
    /// <returns>true if it was in sync</returns>
    private bool Invalidate(TGridItem? goTo, bool explicitGoto = false)
    {
        bool res = _inSync;
        if (res && _data != null)
            _previous = _currentRow < 0 || _currentRow >= _data.Count ? default : _data[_currentRow];
        _inSync = false;
        if (explicitGoto && _data == null)
        {
            _itemToBeRestored = goTo;   // before the first sync there is no current row to capture, only the one asked for
        }
        else if (_data != null && (goTo != null || _itemToBeRestored == null))   // an explicit goto replaces a row an earlier change captured
        {
            _itemToBeRestored = goTo ?? (_currentRow < 0 || _currentRow >= _data.Count ? default : _data[_currentRow]);
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("To be restored: {toBeRestored}", _itemToBeRestored);
        }
        else if (_itemToBeRestored != null && logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Skipping null goto because another one is pending: {pending}", _itemToBeRestored);
        }
        return res;
    }

    /// <summary>
    /// Any modification made to the returned collection is reflected
    /// into the model as soon as the Sync() method is directly called or
    /// indirectly called.
    /// Keep in mind that, in presence of filters and/or sorted columns the current-row
    /// cannot be applied here.
    /// </summary>
    /// <returns>The model back collection with all the data.</returns>
    public override AList<TGridItem> GetData()
    {
        return _orgData;
    }

    /// <returns>A snapshot list reflecting any sort order or filter applied, it is 
    ///   unmodifiable but can change if the backend collection changes.
    /// </returns>
    public sealed override IReadOnlyList<TGridItem> GetDataView()
    {
        SyncImpl();
        return _data!;
    }

    /// <inheritdoc/>
    protected override int ViewIndexToIndex(bool insert, int viewIndex)
    {
        if (insert && viewIndex >= GetRowCount())
            return _orgData.Count;
        TGridItem item = GetItem(viewIndex);
        int pos = Utils.IdentityIndexOf(_orgData, item);
        if (pos < 0)
        {
            if (insert)
                pos = _orgData.Count;
            else
                throw new InvalidOperationException($"Cannot convert: {viewIndex} item: {item} to a real index");
        }
        return pos;
    }

    /// <inheritdoc/>
    public override void Sync()
    {
        SyncImpl();
    }

    /// <inheritdoc/>
    public override int GetDataVersion()
    {
        return _version;
    }

    /// <summary>
    /// Used for sorting.
    /// </summary>
    protected virtual IComparer<object>? GetComparerForColumn(int columnIdx)
    {
        return PocoColumnComparer.GetComparerForColumn(GetColumn(columnIdx));
    }

    /// <inheritdoc/>
    protected override void ValidateStoredColumn(int columnIndex)
    {
        if (GetColumn(columnIndex).IsSortable())
            PocoColumnComparer.CheckCanSort(GetColumnName(columnIndex), GetColumn(columnIndex).GetValueType(), GetComparerForColumn(columnIndex));
    }

    /// <summary>Brings the view up to date with the data, filters and sort, once after each change, and finds the current row again.</summary>
    protected void SyncImpl()
    {
        if (_inSync)
            return;
        ++_version;
        _inSync = true;
        _filtersDefs.Sync();
        TGridItem? itemToBeRestored = _itemToBeRestored;
        _itemToBeRestored = default;
        TGridItem? previous = _previous;
        _previous = default;
        if (_sortedColumn == null && _filtersDefs.Count <= 0)
        {
            _data = _orgData.AsReadOnly();
        }
        else if (_filtersDefs.Count <= 0)
        {
            List<TGridItem> ndata = new(_orgData);
            _data = ndata.AsReadOnly();
            int colIdx = GetColumnIndex(_sortedColumn.RequireNotNull());
            SortStable(ndata, new ColumnComparer<TGridItem>(this, GetComparerForColumn(colIdx), _sortedAsc, colIdx));
        }
        else
        {
            InMemoryFilter<TGridItem> memFilter = new(this, false, _filtersDefs.Definitions);
            List<TGridItem> ndata = new(_orgData.Count);
            _data = ndata.AsReadOnly();
            foreach (var el in _orgData)
            {
                if (memFilter.Includes(el))
                    ndata.Add(el);
            }
            if (_sortedColumn != null)
            {
                int colIdx = GetColumnIndex(_sortedColumn.RequireNotNull());
                SortStable(ndata, new ColumnComparer<TGridItem>(this, GetComparerForColumn(colIdx), _sortedAsc, colIdx));
            }
        }
        if (itemToBeRestored != null)
        {
            int pos = RestoreItem(_data, itemToBeRestored);
            if (pos < 0 && previous != null)   // a goto the view drops keeps the row that was current
                pos = RestoreItem(_data, previous);
            if (pos >= 0)
                _currentRow = pos;
        }
        _currentRow = Math.Max(-1, Math.Min(_currentRow, _data.Count - 1));
        FireModelChanged(GridModelChangeType.AfterSync);
    }

    // List.Sort is unstable: rows with equal values would change places on every change to the list
    private static void SortStable(List<TGridItem> list, IComparer<TGridItem> comparer)
    {
        TGridItem[] sorted = [.. list.Order(comparer)];
        list.Clear();
        list.AddRange(sorted);
    }

    /// <inheritdoc/>
    public override void SetCurrentRow(int rowIndex)
    {
        SyncImpl();
        if (rowIndex == _currentRow)
            return;
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        if (rowIndex < -1 || rowIndex >= _data!.Count)
            throw new ArgumentOutOfRangeException(nameof(rowIndex), $"Row index {rowIndex} out of range: {_data!.Count}");
        _currentRow = rowIndex;
        FireModelChanged(GridModelChangeType.Other);
    }

    /// <inheritdoc/>
    public override int GetCurrentRow()
    {
        SyncImpl();
        return _currentRow;
    }

    /// <inheritdoc/>
    public override TGridItem GetItem(int rowIndex)
    {
        SyncImpl();
        return _data![rowIndex];
    }

    /// <inheritdoc/>
    public override int GetCurrentRowCount()
    {
        SyncImpl();
        return _data!.Count;
    }

    /// <inheritdoc/>
    public override int GetRowCount()
    {
        SyncImpl();
        return _data!.Count;
    }

    /// <inheritdoc/>
    public bool IsFilterable()
    {
        return true;
    }

    /// <inheritdoc/>
    public void ApplyFilters(IEnumerable<KeyValuePair<string, AFilterDef>> filters)
    {
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        var checkedFilters = CheckFilterColumns(filters);
        _filtersDefs.Clear();
        foreach (var entry in checkedFilters)
        {
            _filtersDefs.Add(entry.Key, entry.Value.Clone());
        }
        Invalidate();
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, AFilterDef> GetFilters()
    {
        return _filtersDefs.Definitions;
    }

    /// <inheritdoc/>
    public bool IsFiltered()
    {
        return _filtersDefs.IsFiltered();
    }

    /// <inheritdoc/>
    public bool IsSortable()
    {
        return true;
    }

    /// <inheritdoc/>
    public string? GetSortedColumn()
    {
        return _sortedColumn;
    }

    /// <inheritdoc/>
    public SortType GetSortType()
    {
        return _sortedColumn == null ? SortType.None : (_sortedAsc ? SortType.Asc : SortType.Desc);
    }

    /// <inheritdoc/>
    public void SetSortedColumn(string? columnName, bool ascending)
    {
        if (_sortedColumn == columnName && (columnName == null || _sortedAsc == ascending))   // no sort has no direction
            return;
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        if (columnName != null && GetColumnIndex(columnName) < 0)
            throw new ArgumentException($"Column not found: {columnName}");
        if (columnName != null)   // a column the user cannot sort may still be sorted in code
            PocoColumnComparer.CheckCanSort(columnName, GetColumn(GetColumnIndex(columnName)).GetValueType(), GetComparerForColumn(GetColumnIndex(columnName)));
        _sortedColumn = columnName;
        _sortedAsc = ascending;
        Invalidate();
    }

    /// <inheritdoc/>
    /// <remarks>The token is ignored here: the data is already in memory, so there is no fetch to cancel.</remarks>
    /// <exception cref="InvalidOperationException">Thrown if there is no sorted column, or the current row is frozen.</exception>
    public ValueTask<int> SearchOnSortedColumnAsync(object? value, CancellationToken ct = default)
    {
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        if (_sortedColumn == null)
            throw new InvalidOperationException($"No sorted column");
        SyncImpl();
        int colIdx = GetColumnIndex(_sortedColumn);
        return ValueTask.FromResult(PocoColumnComparer.BinarySearch(_data!, this, colIdx, value,
            _sortedAsc, GetComparerForColumn(colIdx)));
    }

    /// <inheritdoc/>
    public bool CanSearchOnSortedColumn()
    {
        return true;
    }

    /// <inheritdoc/>
    /// <remarks>The token is ignored here: the data is already in memory, so there is no fetch to cancel.</remarks>
    /// <exception cref="ArgumentException">Thrown if the column does not exist.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the current row is frozen.</exception>
    public ValueTask<int> ContainsSearchAsync(string columnName, string value, bool forward, int startFrom, CancellationToken ct = default)
    {
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        int colIdx = GetColumnIndex(columnName);
        if (colIdx < 0)
            throw new ArgumentException($"Column not found: {columnName}");
        SyncImpl();
        return ValueTask.FromResult(PocoColumnComparer.ContainsSearch(_data!, this, colIdx, value, forward, startFrom));
    }

    /// <inheritdoc/>
    public bool CanDoContainsSearch()
    {
        return true;
    }

    // ////

    /// <summary>
    /// The list <see cref="GetData"/> returns: a change to it invalidates the model, which applies its filters and sort again at the
    /// next sync.
    /// </summary>
    private class MyList<CC> : AList<CC> where CC : class
    {
        private readonly DataGridModel<CC> _model;

        internal MyList(DataGridModel<CC> model, IList<CC> list) : base(list)
        {
            this._model = model.RequireNotNull();
        }

        protected override void OnChange(CC? newItem)
        {
            _model.Invalidate(newItem);
        }

        protected override void OnGoto(CC? item)
        {
            _model.Invalidate(item, explicitGoto: true);
        }
    }
}

using Glazor.Grds.Filters;
using Glazor.Grds.Props;
using Glazor.Misc;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace Glazor.Grds.Mdls;

/// <summary>
/// Base implementation of a grid model.
/// </summary>
public abstract class AGridModel<TGridItem> : IGridModel<TGridItem>
{
    private readonly VersionedList<ColumnInfo<TGridItem>> _columnInfos = new(new List<ColumnInfo<TGridItem>>(), ColumnInfoEqualityComparer<TGridItem>.Instance);

    /// <inheritdoc/>
    public event OnModelChangedEventHandler? OnModelChanged;

    /// <inheritdoc/>
    public Type GetItemType() => typeof(TGridItem);

    /// <inheritdoc/>
    public void EnrichColumn<T>(AGrid<TGridItem> grid, EnrichGridColumn<TGridItem, T> gridColumn)
    {
        if (!grid.CheckCollectingColumns(gridColumn.Name))
            return;
        string columnName = gridColumn.Name;
        // I'm assuming here we are not having too many columns in here
        for (int i = 0; i < _columnInfos.Count; i++)
        {
            ColumnInfo<TGridItem> ci = _columnInfos[i];
            if (ci.Name == columnName)
            {
                _columnInfos[i] = new ColumnInfo<TGridItem>(columnName, ci.Column.Enrich<T>(gridColumn), ci.Property, ci.PropertyExpression);
                return;
            }
        }
        throw new ArgumentException($"Column doesn't exist: {columnName}");
    }

    /// <inheritdoc/>
    public void SetColumn<T>(AGrid<TGridItem> grid, GridColumn<TGridItem, T> gridColumn)
    {
        if (!grid.CheckCollectingColumns(gridColumn.Name))
            return;
        string columnName = gridColumn.Name;
        var column = gridColumn.GetColumn();
        grid.ValidateColumn(columnName, column);
        if (gridColumn.EFProperty != null)
        {
            RegisterEFColumnImpl(false, columnName, column, gridColumn.EFProperty);
        }
        else if (gridColumn.ReadFn != null)
        {
            RegisterColumnImpl(false, columnName, column, gridColumn.ReadFn);
        }
        else
        {
            throw new ArgumentException($"Neither {nameof(gridColumn.EFProperty)} nor {nameof(gridColumn.Property)} has been specified for column: {columnName}");
        }
    }

    /// <summary>
    /// A "computed" column, i.e. you provide the lambda expression to access the property value.
    /// </summary>
    /// <exception cref="ArgumentException"></exception>
    public AGridModel<TGridItem> AddColumn<TProp>(string columnName, IColumn<TGridItem, TProp> column, Func<TGridItem, TProp> fprop)
    {
        RegisterColumnImpl<TProp>(true, columnName, column, fprop);
        return this;
    }

    private void RegisterColumnImpl<TProp>(bool addOnly, string columnName, IColumn<TGridItem, TProp> column, Func<TGridItem, TProp> fprop)
    {
        int index = _columnInfos.FindIndex(ci => ci.Name == columnName);
        if (addOnly && index >= 0)
            throw new ArgumentException($"Column already exists: {columnName}");
        var cinfo = ColumnInfo<TGridItem>.Of(columnName, column, fprop);
        Store(index, cinfo);
    }

    /// <summary>
    /// A "computed" column, i.e. you provide the lambda expression to access the property value, this is possibly pushed down to the db
    /// if an EF model is used.
    /// </summary>
    /// <param name="columnName">The column's name, unique in the model.</param>
    /// <param name="column">The column's declaration: label, size and flags.</param>
    /// <param name="eprop">Lambda expression reaching the property value; pushed down to the db with an EF model.</param>
    /// <exception cref="ArgumentException"></exception>
    public AGridModel<TGridItem> AddEFColumn<TProp>(string columnName, IColumn<TGridItem, TProp> column, Expression<Func<TGridItem, TProp>> eprop)
    {
        RegisterEFColumnImpl<TProp>(true, columnName, column, eprop);
        return this;
    }

    private void RegisterEFColumnImpl<TProp>(bool addOnly, string columnName, IColumn<TGridItem, TProp> column, Expression<Func<TGridItem, TProp>> eprop)
    {
        int index = _columnInfos.FindIndex(ci => ci.Name == columnName);
        if (addOnly && index >= 0)
            throw new ArgumentException($"Column already exists: {columnName}");
        var cinfo = ColumnInfo<TGridItem>.Of(columnName, column, eprop);
        Store(index, cinfo);
    }

    // Checked in place, since a model's comparer for a column is looked up by its index
    private void Store(int index, ColumnInfo<TGridItem> cinfo)
    {
        if (index < 0)
            _columnInfos.Add(cinfo);
        else
            _columnInfos[index] = cinfo;
        ValidateStoredColumn(index < 0 ? _columnInfos.Count - 1 : index);
    }

    /// <summary>Throws when the model cannot hold the column just stored at <paramref name="columnIndex"/>; accepts any by default.</summary>
    protected virtual void ValidateStoredColumn(int columnIndex)
    {
    }

    /// <inheritdoc/>
    public void UnsetColumn(AGrid<TGridItem> grid, string columnName) => RemoveColumn(columnName);

    /// <summary>Takes the column out of the model; a data model drops its filter and its sort too.</summary>
    /// <returns>The column removed, or null when the model has no such column.</returns>
    public virtual IColumn<TGridItem>? RemoveColumn(string columnName)
    {
        int idx = GetColumnIndex(columnName);
        if (idx < 0)
            return null;
        var cinfo = _columnInfos[idx];
        _columnInfos.RemoveAt(idx);
        return cinfo.Column;
    }

    /// <inheritdoc/>
    public int GetColumnIndex(string columnName)
    {
        for (int i = 0; i < _columnInfos.Count; i++)
        {
            if (_columnInfos[i].Name == columnName)
                return i;
        }
        return -1;
    }

    // Called by ApplyFilters before it clears the filters, so that a call it refuses leaves them as they were
    private protected List<KeyValuePair<string, AFilterDef>> CheckFilterColumns(IEnumerable<KeyValuePair<string, AFilterDef>> filters)
    {
        List<KeyValuePair<string, AFilterDef>> res = [.. filters];
        HashSet<string> seen = [];
        foreach (var (columnName, _) in res)
        {
            if (GetColumnIndex(columnName) < 0)
                throw new ArgumentException($"Column not found: {columnName}", nameof(filters));
            if (!seen.Add(columnName))
                throw new ArgumentException($"Column filtered twice: {columnName}", nameof(filters));
        }
        return res;
    }

    /// <inheritdoc/>
    public IColumnInfo<TGridItem> GetColumnInfo(int columnIndex) => _columnInfos[columnIndex];
    /// <inheritdoc/>
    public IColumn<TGridItem> GetColumn(int columnIndex) => _columnInfos[columnIndex].Column;

    /// <inheritdoc/>
    public int GetColumnCount() => _columnInfos.Count;

    /// <inheritdoc/>
    public int GetColumnsVersion() => _columnInfos.Version;

    /// <inheritdoc/>
    public string GetColumnName(int columnIndex) => _columnInfos[columnIndex].Name;

    /// <inheritdoc/>
    public virtual object? GetValueAt(int rowIndex, int columnIndex)
    {
        TGridItem item = GetItem(rowIndex);
        return GetValue(item, columnIndex);
    }

    /// <inheritdoc/>
    public virtual bool IsCurrentRowFrozen() => false;

    /// <inheritdoc/>
    public abstract int GetCurrentRowCount();

    /// <inheritdoc/>
    public abstract int GetRowCount();

    /// <inheritdoc/>
    public abstract bool RequiresFetch();

    /// <inheritdoc/>
    public abstract ValueTask<int> FetchRowsAsync(int rowIndex, int count, CancellationToken ct = default);

    /// <inheritdoc/>
    public virtual void Sync()
    {
    }

    /// <inheritdoc/>
    public abstract int GetDataVersion();

    /// <inheritdoc/>
    public virtual object? GetValue(TGridItem item, int columnIndex)
    {
        IGetter<TGridItem, object>? prop = GetProperty(columnIndex);
        if (prop == null)
            return null;
        return prop.GetterFunc.Invoke(item);
    }

    /// <inheritdoc/>
    public abstract TGridItem GetItem(int rowIndex);

    /// <summary>
    /// For paged models it acts on loaded data only.
    /// </summary>
    public virtual int RowIndexOf(TGridItem item)
    {
        // fast path ...
        int cur = GetCurrentRow();
        if (cur >= 0 && cur < GetCurrentRowCount() && SameItem(item, GetItem(cur)))
            return cur;
        var pkFn = GetItemKeyFn();
        if (pkFn == null)
        {
            for (int i = 0; i < GetCurrentRowCount(); i++)
            {
                TGridItem pp = GetItem(i);
                if (ReferenceEquals(pp, item) || item!.Equals(pp))
                    return i;
            }
        }
        else
        {
            var itemPK = pkFn.Invoke(item);
            for (int i = 0; i < GetCurrentRowCount(); i++)
            {
                TGridItem pp = GetItem(i);
                if (ReferenceEquals(pp, item) || Object.Equals(itemPK, pkFn.Invoke(pp)))
                    return i;
            }
        }
        return -1;
    }

    /// <inheritdoc/>
    public virtual bool SameItem(TGridItem? item1, TGridItem? item2)
    {
        if (ReferenceEquals(item1, item2))  // both null included
            return true;
        if (item1 == null || item2 == null)
            return false;
        var pkFn = GetItemKeyFn();
        if (pkFn == null)
            return item1.Equals(item2);
        var item1PK = pkFn.Invoke(item1);
        var item2PK = pkFn.Invoke(item2);
        return Object.Equals(item1PK, item2PK);   // a key decides alone, as in RestoreItem
    }

    /// <inheritdoc/>
    public virtual TGridItem? GetCurrentItem()
    {
        int idx = GetCurrentRow();
        return idx < 0 ? default : GetItem(idx);
    }

    /// <inheritdoc/>
    public void SetCurrentItem(TGridItem item)
    {
        TGridItem? current = GetCurrentItem();
        if (current != null && SameItem(current, item))
            return;
        int idx = RowIndexOf(item);
        if (idx < 0)
            throw new ArgumentException($"Item not found in the model: {item}");
        SetCurrentRow(idx);
    }

    /// <inheritdoc/>
    public abstract int GetCurrentRow();

    /// <inheritdoc/>
    public abstract IReadOnlyList<TGridItem> GetDataView();

    /// <summary>Reads the column's value from a row, in memory; throws when the column does not exist.</summary>
    public IGetter<TGridItem, object>? GetProperty(int columnIndex) => _columnInfos[columnIndex].Property;

    /// <summary>
    /// When not null the given column can be used to "push" queries down to EF.
    /// </summary>
    public Expression<Func<TGridItem, object?>>? GetPropertyExpression(int columnIndex) => _columnInfos[columnIndex].PropertyExpression;

    /// <inheritdoc/>
    public abstract void SetCurrentRow(int rowIndex);

    /// <summary>The index in <paramref name="data"/> of <paramref name="itemToBeRestored"/>: the same instance, else the row with its key, or an equal row when the model has no key; -1 when none is there.</summary>
    protected virtual int RestoreItem(IReadOnlyList<TGridItem> data, TGridItem itemToBeRestored)
    {
        int pos = Utils.IdentityIndexOf(data, itemToBeRestored);
        if (pos >= 0)
            return pos;
        var pkFn = GetItemKeyFn();
        if (pkFn == null)
        {
            for (int i = 0; i < data.Count; i++)
                if (itemToBeRestored!.Equals(data[i]))
                    return i;
            return -1;
        }
        // If there is a "primary key" use it
        object itemToBeRestoredPK = pkFn.Invoke(itemToBeRestored);
        for (int i = 0; i < data.Count; i++)
        {
            TGridItem t = data[i];
            object tPK = pkFn.Invoke(t);
            if (Object.Equals(itemToBeRestoredPK, tPK))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// If the item has a sort of primary key, ie. something that uniquely identifies it, return it here.
    /// </summary>
    public abstract Func<TGridItem, object>? GetItemKeyFn();

    /// <summary>
    /// Synchronously fires the OnModelChanged event.
    /// </summary>
    protected void FireModelChanged(GridModelChangeType changeType)
    {
        OnModelChanged?.Invoke(changeType);
    }
}

using Glazor.Misc;
using Microsoft.AspNetCore.Components;

namespace Glazor.Grds.Mdls;

/// <summary>
/// An in-memory model whose rows the user arranges by hand: an ordered list, with no filter and no sort. See docs/data-models.md.
/// </summary>
public class GridModel<TGridItem> : AMemGridModel<TGridItem>, IOrderedGridModel<TGridItem> where TGridItem : class
{
    private readonly MyList<TGridItem> _data;

    private TGridItem? _itemToBeRestored;
    private TGridItem? _previous;   // the current row before the first change since the sync, as the list is changed in place
    private int _currentRow;

    private bool _inSync;
    private int _version;

    /// <summary>
    /// Gives a row's key, which finds the row again after a reload; without it rows are compared with <c>Equals</c>. An
    /// editable model requires it.
    /// </summary>
    public Func<TGridItem, object>? ItemKeyFn { get; init; }

    /// <summary>
    /// Whether the rows at the two indexes may swap places, asked with the model; without it any two may.
    /// <c>MultiSelGrid</c> asks it at every render for the current row and the one above or below it.
    /// </summary>
    public Func<GridModel<TGridItem>, int, int, bool>? AreRowsSwappableFn { get; init; }

    /// <summary>An empty model; fill it through <c>GetData()</c>.</summary>
    public GridModel() : this([])
    {
    }

    /// <summary>A model over a copy of <paramref name="data"/>; change the rows later through <c>GetData()</c>.</summary>
    public GridModel(IEnumerable<TGridItem> data)
    {
        this._data = new MyList<TGridItem>(this, new List<TGridItem>(data));
    }

    /// <inheritdoc/>
    public sealed override Func<TGridItem, object>? GetItemKeyFn() => ItemKeyFn;

    /// <inheritdoc/>
    protected override int ViewIndexToIndex(bool insert, int viewIndex) => viewIndex;

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
        if (res)
            _previous = _currentRow < 0 || _currentRow >= _data.Count ? default : _data[_currentRow];
        _inSync = false;
        if (_version == 0 && !explicitGoto)
            return res;   // before the first sync there is no current row to capture, only one asked for
        if (_data != null && (goTo != null || _itemToBeRestored == null))   // an explicit goto replaces a row an earlier change captured
            _itemToBeRestored = goTo ?? (_currentRow < 0 || _currentRow >= _data.Count ? default : _data[_currentRow]);
        return res;
    }

    /// <summary>
    /// Any modification made to the returned collection is reflected
    /// into the model as soon as the Sync() method is directly called or
    /// indirectly called.
    /// </summary>
    /// <returns>the model back-end collection.</returns>
    public override AList<TGridItem> GetData()
    {
        return _data;
    }

    /// <summary>Whether the two rows may swap places: <see cref="AreRowsSwappableFn"/> decides, and without it any two may.</summary>
    public bool AreRowsSwappable(int row1, int row2)
    {
        return AreRowsSwappableFn == null || AreRowsSwappableFn(this, row1, row2);
    }

    /// <inheritdoc/>
    public void SwapRows(int row1, int row2)
    {
        if (row1 == row2)
            return;
        if (IsCurrentRowFrozen())
            throw new InvalidOperationException("A pending edit or insert holds the current row");
        if (row1 < 0 || row1 >= _data.Count)
            throw new ArgumentException("row1 out of bounds: " + row1);
        if (row2 < 0 || row2 >= _data.Count)
            throw new ArgumentException("row2 out of bounds: " + row2);
        if (!AreRowsSwappable(row1, row2))
            throw new ArgumentException($"Not swappable rows: {row1} {row2}");
        TGridItem p1 = _data[row1];
        TGridItem p2 = _data[row2];
        _data[row1] = p2;
        _data[row2] = p1;
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
    public override int GetDataVersion()
    {
        return _version;
    }

    /// <inheritdoc/>
    public override TGridItem GetItem(int rowIndex)
    {
        SyncImpl();
        return _data![rowIndex];
    }

    /// <inheritdoc/>
    public override int GetCurrentRow()
    {
        SyncImpl();
        return _currentRow;
    }

    /// <inheritdoc/>
    public override IReadOnlyList<TGridItem> GetDataView()
    {
        SyncImpl();
        return _data!.AsReadOnly();
    }

    /// <inheritdoc/>
    public override void Sync()
    {
        SyncImpl();
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

    /// <summary>Brings the view up to date with the data, once after each change, and finds the current row again.</summary>
    protected void SyncImpl()
    {
        if (_inSync)
            return;
        ++_version;
        _inSync = true;
        TGridItem? itemToBeRestored = _itemToBeRestored;
        _itemToBeRestored = default;
        TGridItem? previous = _previous;
        _previous = default;
        if (itemToBeRestored != null)
        {
            int pos = RestoreItem(_data, itemToBeRestored);
            if (pos < 0 && previous != null)   // a goto to a row the list lacks keeps the row that was current
                pos = RestoreItem(_data, previous);
            if (pos >= 0)
                _currentRow = pos;
        }
        _currentRow = Math.Max(-1, Math.Min(_currentRow, _data.Count - 1));
        FireModelChanged(GridModelChangeType.AfterSync);
    }

    // ////

    /// <summary>
    /// The list <see cref="GetData"/> returns: a change to it invalidates the model.
    /// </summary>
    private class MyList<CC> : AList<CC> where CC : class
    {
        private readonly GridModel<CC> _model;

        internal MyList(GridModel<CC> model, IList<CC> list) : base(list)
        {
            this._model = model.RequireNotNull();
        }

        protected override void OnChange(CC? newItem)
        {
            _model.Invalidate(newItem);
        }

        protected override void OnGoto(CC? item)
        {
            _model.Invalidate(item, explicitGoto: true);   // the list is the model's data, so a goto holds before the first sync too
        }
    }
}

using Glazor.Misc;

namespace Glazor.Grds.Mdls;

internal class EditSupport<TGridItem> where TGridItem : class
{
    private TGridItem? _newItem;
    private TGridItem? _editItem;
    private object? _editKey;   // taken when the edit starts: the form changes the item in place, key members included
    private int _insertRow;

    private readonly Func<int, object?, CancellationToken, ValueTask> _reloadItemFn;   // row, and the key to reload it by (null: the row's own)

    private readonly Func<TGridItem, CancellationToken, ValueTask> _reloadAllAndRestoreFn;

    private readonly IUpdatableModel<TGridItem> _updModel;

    internal EditSupport(Func<int, object?, CancellationToken, ValueTask> reloadItemFn, Func<TGridItem, CancellationToken, ValueTask> reloadAllAndRestoreFn, IUpdatableModel<TGridItem> updModel)
    {
        _reloadItemFn = reloadItemFn;
        _reloadAllAndRestoreFn = reloadAllAndRestoreFn;
        _updModel = updModel;
    }

    /// <summary>
    /// To be used when we can load a single item from the external storage.
    /// </summary>
    /// <param name="model">The model edited.</param>
    /// <param name="reloadAllFn">Reads every row again from the store; the model finds the current item in them itself</param>
    /// <param name="reloadItemFn">Reads again the row the item names, by its key, or null when it is gone</param>
    internal EditSupport(AMemGridModel<TGridItem> model, Func<ValueTask<IList<TGridItem>>> reloadAllFn,
        Func<TGridItem, ValueTask<TGridItem?>> reloadItemFn)
    {
        _updModel = model;
        _reloadAllAndRestoreFn = async (current, ct) =>
        {
            ct.ThrowIfCancellationRequested();   // the host's functions take no token
            IList<TGridItem> data = await reloadAllFn.Invoke();
            model.GetData().SetAndGoto(data, current);
        };
        _reloadItemFn = async (idx, key, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            Func<TGridItem, object> keyFn = KeyFn(model);
            TGridItem current = model.GetItem(idx).RequireNotNull();
            if (key != null && !Equals(key, keyFn(current)))
            {
                // The host reads the row the item names, and a cancelled edit of key members names another one
                await ReloadAllAndGoto(model, reloadAllFn, current, key);
                return;
            }
            TGridItem? reloaded = await reloadItemFn.Invoke(current);
            if (reloaded != null)
            {
                _updModel.Set(idx, reloaded);
            }
            else
            {
                _updModel.Remove(idx);
            }
        };
    }

    /// <summary>
    /// This should be used when we cannot load a single item from the external storage.
    /// </summary>
    /// <param name="model">The model edited.</param>
    /// <param name="reloadAllFn">Reads every row again from the store; the model finds the current item in them itself</param>
    internal EditSupport(AMemGridModel<TGridItem> model, Func<ValueTask<IList<TGridItem>>> reloadAllFn)
    {
        _updModel = model;
        _reloadAllAndRestoreFn = async (current, ct) =>
        {
            ct.ThrowIfCancellationRequested();   // the host's functions take no token
            IList<TGridItem> data = await reloadAllFn.Invoke();
            model.GetData().SetAndGoto(data, current);
        };
        _reloadItemFn = async (idx, key, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            await ReloadAllAndGoto(model, reloadAllFn, model.GetItem(idx).RequireNotNull(), key);
        };
    }

    // The key finds the row again after a reload, so an editable model cannot do without one; checked when an edit or an
    // insert starts, not at the post that would need it
    private static Func<TGridItem, object> KeyFn(IGridModel<TGridItem> model) =>
        model.GetItemKeyFn() ?? throw new InvalidOperationException("ItemKeyFn is required by an editable model");

    // Goes back to the row stored under key, which a cancelled edit of key members no longer finds by the item's own key
    private static async ValueTask ReloadAllAndGoto(AMemGridModel<TGridItem> model, Func<ValueTask<IList<TGridItem>>> reloadAll,
        TGridItem current, object? key)
    {
        IList<TGridItem> data = await reloadAll.Invoke();
        Func<TGridItem, object> keyFn = KeyFn(model);
        model.GetData().SetAndGoto(data, key == null ? current : data.FirstOrDefault(d => Equals(keyFn(d), key)) ?? current);
    }

    internal TGridItem? InsertingItem() => _newItem;

    internal int InsertRow() => _insertRow;

    // The row being inserted is shown at _insertRow but is not in the backing list: the models map the rows the grid sees
    // to the rows the list holds through these, so the rule lives here once
    internal int CountWithPending(int count) => IsInsertPending() && count != int.MaxValue ? count + 1 : count;

    internal bool IsPendingRow(int rowIndex) => IsInsertPending() && rowIndex == _insertRow;

    internal int BackingIndex(int rowIndex) => IsInsertPending() && rowIndex > _insertRow ? rowIndex - 1 : rowIndex;

    internal bool IsPendingItem(TGridItem item) => IsInsertPending() && ReferenceEquals(item, _newItem);

    internal int CurrentRow(int baseCurrentRow) => IsInsertPending() ? _insertRow : baseCurrentRow;   // the row being inserted, after the current row too

    public virtual async ValueTask CancelAsync(CancellationToken ct = default)
    {
        if (_newItem != null)
        {
            _newItem = null;
            _insertRow = -1;
        }
        else if (_editItem != null)
        {
            _editItem = null;
            int currentRow = _updModel.GetCurrentRow();
            await _reloadItemFn.Invoke(currentRow, _editKey, ct);
        }
        else
            return;
        _updModel.FireModelChanged(GridModelChangeType.Other);
    }

    public virtual async ValueTask<TGridItem> EditAsync()
    {
        if (IsInsertPending())
            throw new InvalidOperationException("An insert is pending");
        if (IsEditPending())
            throw new InvalidOperationException("Edit is pending");
        Func<TGridItem, object> keyFn = KeyFn(_updModel);
        _editItem = _updModel.GetCurrentItem() ?? throw new InvalidOperationException("No current row");
        _editKey = keyFn.Invoke(_editItem);
        _updModel.FireModelChanged(GridModelChangeType.Other);
        return _editItem;
    }

    public virtual async ValueTask<TGridItem> InsertAsync(TGridItem newItem, bool afterCurrentRow)
    {
        if (IsInsertPending())
            throw new InvalidOperationException("An insert is pending");
        if (IsEditPending())
            throw new InvalidOperationException("Edit is pending");
        KeyFn(_updModel);
        if (_updModel.GetRowCount() <= 0)
        {
            _insertRow = 0;
        }
        else
        {
            _updModel.GetCurrentItem();   // a paged model reads up to the current row, or moves it to the last one
            // with no current row both directions insert at the top
            _insertRow = Math.Max(0, _updModel.GetCurrentRow() + (afterCurrentRow ? 1 : 0));
        }
        _newItem = newItem.RequireNotNull();
        _updModel.FireModelChanged(GridModelChangeType.Other);
        return _newItem;
    }

    public virtual async ValueTask PostAsync(CancellationToken ct = default)
    {
        if (_newItem == null && _editItem == null)
            throw new InvalidOperationException("Nothing to post");
        if (_newItem != null)
        {
            var newItem = _newItem;
            int insertRow = _insertRow;
            _newItem = null;   // before the insert: the view must map the row the new one displaces, not the pending one
            _insertRow = -1;
            _updModel.Insert(insertRow, newItem);
            // The active filter may exclude the new row, in which case the view did not grow and insertRow is past its end.
            _updModel.SetCurrentRow(Math.Min(insertRow, _updModel.GetRowCount() - 1));
            await _reloadAllAndRestoreFn.Invoke(newItem, ct);
        }
        else
        {
            TGridItem editItem = _editItem.RequireNotNull();
            _editItem = null;
            // A sort in memory during the edit can move the item off the current row, and a re-read can put a copy in its place:
            // reload its own row, by the key it is stored under now
            int row = _updModel.RowIndexOf(editItem);
            await _reloadItemFn.Invoke(row >= 0 ? row : _updModel.GetCurrentRow(), KeyFn(_updModel).Invoke(editItem), ct);
        }
        _updModel.FireModelChanged(GridModelChangeType.Other);
    }

    public virtual bool IsEditPending()
    {
        return _editItem != null;
    }

    public virtual bool IsInsertPending()
    {
        return _newItem != null;
    }
 
}
